using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace KanaBootstrapper;

internal sealed class DiscordRpc : IDisposable
{
	private const int OpHandshake = 0;

	private const int OpFrame = 1;

	private const int OpClose = 2;

	private const int OpPing = 3;

	private const int OpPong = 4;

	private static readonly TimeSpan ResendEvery = TimeSpan.FromSeconds(30.0);

	private readonly string _clientId;

	private readonly int _pid = Process.GetCurrentProcess().Id;

	private readonly object _writeLock = new object();

	private DiscordActivity _activity;

	private NamedPipeClientStream _pipe;

	private volatile bool _ready;

	private volatile bool _dirty;

	private volatile bool _stopped;

	private Thread _worker;

	private bool _disposed;

	public DiscordRpc(string clientId)
	{
		_clientId = clientId ?? string.Empty;
	}

	public void Start()
	{
		if (_worker == null)
		{
			_worker = new Thread(Loop)
			{
				IsBackground = true,
				Name = "DiscordRpc"
			};
			_worker.Start();
		}
	}

	public void Update(DiscordActivity activity)
	{
		lock (_writeLock)
		{
			_activity = activity;
			_dirty = true;
		}
	}

	private void Loop()
	{
		DateTime dateTime = DateTime.MinValue;
		while (!_stopped)
		{
			NamedPipeClientStream pipe = _pipe;
			if (pipe == null)
			{
				Connect();
				dateTime = DateTime.MinValue;
			}
			else if (_ready && (_dirty || dateTime == DateTime.MinValue || DateTime.UtcNow - dateTime >= ResendEvery))
			{
				if (TrySendActivity(pipe))
				{
					_dirty = false;
					dateTime = DateTime.UtcNow;
				}
				else
				{
					Drop(pipe);
				}
			}
			Thread.Sleep(400);
		}
	}

	private void Connect()
	{
		for (int i = 0; i < 10; i++)
		{
			if (_stopped)
			{
				break;
			}
			NamedPipeClientStream namedPipeClientStream = null;
			try
			{
				namedPipeClientStream = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
				namedPipeClientStream.Connect(150);
				_ready = false;
				WriteFrame(namedPipeClientStream, 0, "{\"v\":1,\"client_id\":\"" + JsonEscape(_clientId) + "\"}");
				_pipe = namedPipeClientStream;
				StartReader(namedPipeClientStream);
				Installer.Log("discord: connected to discord-ipc-" + i);
				return;
			}
			catch
			{
				try
				{
					namedPipeClientStream?.Dispose();
				}
				catch
				{
				}
			}
		}
		Thread.Sleep(2500);
	}

	private void StartReader(NamedPipeClientStream pipe)
	{
		Thread thread = new Thread((ThreadStart)delegate
		{
			ReadLoop(pipe);
		});
		thread.IsBackground = true;
		thread.Name = "DiscordRpcReader";
		thread.Start();
	}

	private void ReadLoop(NamedPipeClientStream pipe)
	{
		byte[] array = new byte[8];
		try
		{
			while (!_stopped)
			{
				ReadExact(pipe, array, 8);
				int num = BitConverter.ToInt32(array, 0);
				int num2 = BitConverter.ToInt32(array, 4);
				if (num2 < 0 || num2 > 4194304)
				{
					break;
				}
				byte[] array2 = new byte[num2];
				ReadExact(pipe, array2, num2);
				string text = Encoding.UTF8.GetString(array2);
				switch (num)
				{
				case 3:
					WriteFrame(pipe, 4, text);
					break;
				case 1:
					if (text.IndexOf("\"evt\":\"READY\"", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						_ready = true;
					}
					else if (text.IndexOf("\"evt\":null", StringComparison.OrdinalIgnoreCase) < 0)
					{
						Installer.Log("discord: " + ((text.Length > 300) ? text.Substring(0, 300) : text));
					}
					break;
				case 2:
					return;
				}
			}
		}
		catch
		{
		}
		finally
		{
			Drop(pipe);
		}
	}

	private static void ReadExact(Stream stream, byte[] buffer, int count)
	{
		int num;
		for (int i = 0; i < count; i += num)
		{
			num = stream.Read(buffer, i, count - i);
			if (num <= 0)
			{
				throw new EndOfStreamException();
			}
		}
	}

	private bool TrySendActivity(NamedPipeClientStream pipe)
	{
		try
		{
			WriteFrame(pipe, 1, BuildSetActivity(withActivity: true));
			return true;
		}
		catch
		{
			return false;
		}
	}

	private void Drop(NamedPipeClientStream pipe)
	{
		if (_pipe == pipe)
		{
			_pipe = null;
		}
		_ready = false;
		try
		{
			pipe?.Dispose();
		}
		catch
		{
		}
	}

	private string BuildSetActivity(bool withActivity)
	{
		DiscordActivity activity;
		lock (_writeLock)
		{
			activity = _activity;
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("{\"cmd\":\"SET_ACTIVITY\",\"nonce\":\"").Append(Guid.NewGuid().ToString()).Append("\",\"args\":{\"pid\":");
		stringBuilder.Append(_pid);
		stringBuilder.Append(",\"activity\":");
		stringBuilder.Append(withActivity ? ActivityJson(activity) : "null");
		stringBuilder.Append("}}");
		return stringBuilder.ToString();
	}

	private static void WriteFrame(NamedPipeClientStream pipe, int op, string json)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(json ?? string.Empty);
		byte[] array = new byte[8];
		Buffer.BlockCopy(BitConverter.GetBytes(op), 0, array, 0, 4);
		Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length), 0, array, 4, 4);
		lock (pipe)
		{
			pipe.Write(array, 0, array.Length);
			pipe.Write(bytes, 0, bytes.Length);
			pipe.Flush();
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		_stopped = true;
		NamedPipeClientStream pipe = _pipe;
		if (pipe != null)
		{
			try
			{
				WriteFrame(pipe, 1, BuildSetActivity(withActivity: false));
			}
			catch
			{
			}
			Drop(pipe);
		}
		try
		{
			_worker?.Join(1500);
		}
		catch
		{
		}
	}

	private static string ActivityJson(DiscordActivity activity)
	{
		if (activity == null)
		{
			return "null";
		}
		StringBuilder stringBuilder = new StringBuilder("{");
		bool first = true;
		Str(stringBuilder, ref first, "details", Clip(activity.Details, 128));
		Str(stringBuilder, ref first, "state", Clip(activity.State, 128));
		Raw(stringBuilder, ref first, "timestamps", "{\"start\":" + ToUnixMilliseconds(activity.StartedAt) + "}");
		StringBuilder stringBuilder2 = new StringBuilder("{");
		bool first2 = true;
		Str(stringBuilder2, ref first2, "large_image", activity.LargeImage);
		Str(stringBuilder2, ref first2, "large_text", Clip(activity.LargeText, 128));
		Str(stringBuilder2, ref first2, "small_image", activity.SmallImage);
		Str(stringBuilder2, ref first2, "small_text", Clip(activity.SmallText, 128));
		if (stringBuilder2.Length > 1)
		{
			Raw(stringBuilder, ref first, "assets", stringBuilder2.Append('}').ToString());
		}
		if (activity.Buttons.Count > 0)
		{
			StringBuilder stringBuilder3 = new StringBuilder("[");
			int num = 0;
			foreach (DiscordButton button in activity.Buttons)
			{
				if (button != null && !string.IsNullOrEmpty(button.Label) && !string.IsNullOrEmpty(button.Url))
				{
					if (num == 2)
					{
						break;
					}
					if (num > 0)
					{
						stringBuilder3.Append(',');
					}
					stringBuilder3.Append("{\"label\":\"").Append(JsonEscape(Clip(button.Label, 32))).Append("\",\"url\":\"")
						.Append(JsonEscape(Clip(button.Url, 512)))
						.Append("\"}");
					num++;
				}
			}
			stringBuilder3.Append(']');
			if (num > 0)
			{
				Raw(stringBuilder, ref first, "buttons", stringBuilder3.ToString());
			}
		}
		stringBuilder.Append('}');
		return stringBuilder.ToString();
	}

	private static void Str(StringBuilder sb, ref bool first, string name, string value)
	{
		if (!string.IsNullOrEmpty(value))
		{
			Raw(sb, ref first, name, "\"" + JsonEscape(value) + "\"");
		}
	}

	private static void Raw(StringBuilder sb, ref bool first, string name, string rawValue)
	{
		if (!first)
		{
			sb.Append(',');
		}
		first = false;
		sb.Append('"').Append(name).Append("\":")
			.Append(rawValue);
	}

	private static string Clip(string value, int max)
	{
		if (string.IsNullOrEmpty(value))
		{
			return value;
		}
		if (value.Length > max)
		{
			return value.Substring(0, max);
		}
		return value;
	}

	private static long ToUnixMilliseconds(DateTime value)
	{
		return (long)(((value.Kind == DateTimeKind.Utc) ? value : value.ToUniversalTime()) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
	}

	private static string JsonEscape(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length + 8);
		foreach (char c in value)
		{
			switch (c)
			{
			case '"':
				stringBuilder.Append("\\\"");
				continue;
			case '\\':
				stringBuilder.Append("\\\\");
				continue;
			case '\n':
				stringBuilder.Append("\\n");
				continue;
			case '\r':
				stringBuilder.Append("\\r");
				continue;
			case '\t':
				stringBuilder.Append("\\t");
				continue;
			}
			if (c < ' ')
			{
				StringBuilder stringBuilder2 = stringBuilder.Append("\\u");
				int num = c;
				stringBuilder2.Append(num.ToString("x4"));
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}
}
