using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace KanaBootstrapper;

internal class Installer
{
	private readonly HttpClient _http;

	private static readonly TimeSpan ServerStartTimeout = TimeSpan.FromSeconds(90.0);

	public static string RootDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kana");

	public string ManifestUrl => "https://kanade.cfd/client/manifest-v2.json";

	public string ClientDir => Path.Combine(RootDir, "client");

	public static string DownloadsDir => Path.Combine(RootDir, "Downloads");

	public Installer()
	{
		ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
		_http = new HttpClient
		{
			Timeout = TimeSpan.FromMinutes(10.0)
		};
		_http.DefaultRequestHeaders.Add("User-Agent", "KanaBootstrapper/1.0");
	}

	private static string VersionFile(string folder)
	{
		return Path.Combine(folder, ".version");
	}

	private static string InstalledVersion(string folder)
	{
		try
		{
			string path = VersionFile(folder);
			return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
		}
		catch
		{
			return null;
		}
	}

	public string PlayerPath()
	{
		string text = Path.Combine(ClientDir, "KanaPlayerBeta.exe");
		if (File.Exists(text))
		{
			return text;
		}
		return (Directory.Exists(ClientDir) ? Directory.GetFiles(ClientDir, "KanaPlayerBeta.exe", SearchOption.AllDirectories).FirstOrDefault() : null) ?? text;
	}

	public bool IsClientCurrent(string version)
	{
		if (InstalledVersion(ClientDir) == version)
		{
			return File.Exists(PlayerPath());
		}
		return false;
	}

	public async Task<bool> IsArchiveCurrentAsync(CancellationToken ct)
	{
		string text = await RemoteVersionOrNullAsync("https://cdn.kanade.cfd/2020L.zip", ct).ConfigureAwait(continueOnCapturedContext: false);
		if (text == null)
		{
			return InstalledVersion(ClientDir) != null && File.Exists(PlayerPath());
		}
		return InstalledVersion(ClientDir) == text && File.Exists(PlayerPath());
	}

	public async Task InstallArchiveAsync(InstallOptions options, IProgress<Status> progress, CancellationToken ct)
	{
		Directory.CreateDirectory(DownloadsDir);
		string version = await RemoteVersionOrNullAsync("https://cdn.kanade.cfd/2020L.zip", ct).ConfigureAwait(continueOnCapturedContext: false);
		if (version == null || InstalledVersion(ClientDir) != version || !File.Exists(PlayerPath()))
		{
			progress?.Report(new Status("Downloading the 2020 client...", 30));
			string zip = Path.Combine(DownloadsDir, Path.GetFileName(new Uri("https://cdn.kanade.cfd/2020L.zip").AbsolutePath));
			await DownloadAsync("https://cdn.kanade.cfd/2020L.zip", zip, null, ct).ConfigureAwait(continueOnCapturedContext: false);
			progress?.Report(new Status("Unpacking the 2020 client...", 85));
			ReplaceFolder(ClientDir);
			ExtractInto(zip, ClientDir);
			File.WriteAllText(VersionFile(ClientDir), version ?? "2020");
			TryDelete(zip);
		}
		progress?.Report(new Status("Configuring client...", 95));
		ClientPatcher.EnsurePatched(ClientDir);
		progress?.Report(new Status("Finishing up...", 98));
		if (options.RegisterProtocol)
		{
			RegisterProtocol();
		}
		if (options.DesktopShortcut)
		{
			CreateDesktopShortcut(PlayerPath());
		}
		RemoveLegacyLayout();
		progress?.Report(new Status("Ready.", 100));
	}

	public async Task<Manifest> FetchManifestAsync(CancellationToken ct)
	{
		string json;
		using (HttpResponseMessage response = await _http.GetAsync(ManifestUrl, ct).ConfigureAwait(continueOnCapturedContext: false))
		{
			if (!response.IsSuccessStatusCode)
			{
				throw new Exception(DescribeFailure(response.StatusCode, ManifestUrl, "update manifest"));
			}
			json = await response.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		ct.ThrowIfCancellationRequested();
		Manifest manifest = Deserialize<Manifest>(json);
		if (manifest == null)
		{
			throw new Exception("The server sent a manifest this build cannot read.");
		}
		if (!manifest.Available)
		{
			throw new Exception("Downloads are turned off on the server right now.");
		}
		if (manifest.Format != 2)
		{
			throw new Exception("Manifest format " + manifest.Format + " is newer than this bootstrapper. Update it.");
		}
		if (manifest.Packages == null || manifest.Packages.Count == 0)
		{
			throw new Exception("The manifest lists no packages.");
		}
		return manifest;
	}

	public async Task InstallAsync(Manifest manifest, InstallOptions options, IProgress<Status> progress, CancellationToken ct)
	{
		Directory.CreateDirectory(DownloadsDir);
		if (!IsClientCurrent(manifest.Version))
		{
			await InstallClientAsync(manifest, progress, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		progress?.Report(new Status("Finishing up...", 98));
		if (options.RegisterProtocol)
		{
			RegisterProtocol();
		}
		if (options.DesktopShortcut)
		{
			CreateDesktopShortcut(PlayerPath());
		}
		RemoveLegacyLayout();
		progress?.Report(new Status("Ready.", 100));
	}

	private async Task InstallClientAsync(Manifest manifest, IProgress<Status> progress, CancellationToken ct)
	{
		string target = ClientDir;
		ReplaceFolder(target);
		long totalBytes = manifest.Packages.Sum((Package p) => p.Size);
		long doneBytes = 0L;
		object gate = new object();
		Queue<Package> queue = new Queue<Package>(manifest.Packages.OrderByDescending((Package p) => p.Size));
		List<Task> list = new List<Task>();
		Package package;
		for (int num = 0; num < Math.Min(3, manifest.Packages.Count); num++)
		{
			list.Add(Task.Run(async delegate
			{
				string zipPath;
				while (true)
				{
					lock (gate)
					{
						if (queue.Count == 0)
						{
							return;
						}
						package = queue.Dequeue();
					}
					ct.ThrowIfCancellationRequested();
					zipPath = Path.Combine(DownloadsDir, package.Sha256 + ".zip");
					if (!File.Exists(zipPath) || !(Sha256OfFile(zipPath) == package.Sha256))
					{
						await DownloadAsync(package.Url, zipPath, delegate(long read)
						{
							lock (gate)
							{
								doneBytes += read;
								int val = (int)((totalBytes > 0) ? (doneBytes * 90 / totalBytes) : 0);
								progress?.Report(new Status("Downloading " + package.Name + "...", Math.Min(val, 90)));
							}
						}, ct).ConfigureAwait(continueOnCapturedContext: false);
						if (Sha256OfFile(zipPath) != package.Sha256)
						{
							break;
						}
					}
					else
					{
						lock (gate)
						{
							doneBytes += package.Size;
							progress?.Report(new Status("Verified " + package.Name, Math.Min((int)(doneBytes * 90 / Math.Max(totalBytes, 1L)), 90)));
						}
					}
					progress?.Report(new Status("Unpacking " + package.Name + "...", -1));
					ExtractInto(zipPath, target);
				}
				TryDelete(zipPath);
				throw new Exception("Package '" + package.Name + "' arrived damaged. Try again.");
			}, ct));
		}
		await Task.WhenAll(list).ConfigureAwait(continueOnCapturedContext: false);
		File.WriteAllText(VersionFile(target), manifest.Version);
	}

	private static void ReplaceFolder(string folder)
	{
		if (Directory.Exists(folder))
		{
			try
			{
				Directory.Delete(folder, recursive: true);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				throw new Exception("Close Kana first - its files are still open.");
			}
		}
		Directory.CreateDirectory(folder);
	}

	private static void RemoveLegacyLayout()
	{
		try
		{
			string path = Path.Combine(RootDir, "Versions");
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
			string path2 = Path.Combine(RootDir, "client2021");
			if (Directory.Exists(path2))
			{
				Directory.Delete(path2, recursive: true);
			}
		}
		catch
		{
		}
	}

	private async Task<string> RemoteVersionAsync(string url, CancellationToken ct)
	{
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, url);
		using HttpResponseMessage httpResponseMessage = await _http.SendAsync(request, ct).ConfigureAwait(continueOnCapturedContext: false);
		httpResponseMessage.EnsureSuccessStatusCode();
		string text = ((httpResponseMessage.Headers.ETag != null) ? httpResponseMessage.Headers.ETag.Tag : null);
		if (!string.IsNullOrEmpty(text))
		{
			return text.Trim('"', 'W', '/');
		}
		DateTimeOffset? lastModified = httpResponseMessage.Content.Headers.LastModified;
		long? contentLength = httpResponseMessage.Content.Headers.ContentLength;
		return (lastModified.HasValue ? lastModified.Value.UtcTicks.ToString() : "?") + "-" + (contentLength.HasValue ? contentLength.Value.ToString() : "?");
	}

	private async Task<string> RemoteVersionOrNullAsync(string url, CancellationToken ct)
	{
		try
		{
			return await RemoteVersionAsync(url, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
			return null;
		}
	}

	private async Task DownloadAsync(string url, string path, Action<long> onRead, CancellationToken ct)
	{
		string temp = path + ".part";
		TryDelete(temp);
		using (HttpResponseMessage response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(continueOnCapturedContext: false))
		{
			if (!response.IsSuccessStatusCode)
			{
				throw new Exception(DescribeFailure(response.StatusCode, url, "client"));
			}
			using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false);
			using FileStream file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
			byte[] buffer = new byte[81920];
			while (true)
			{
				int num;
				int read = (num = await source.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(continueOnCapturedContext: false));
				if (num <= 0)
				{
					break;
				}
				await file.WriteAsync(buffer, 0, read, ct).ConfigureAwait(continueOnCapturedContext: false);
				onRead?.Invoke(read);
			}
		}
		TryDelete(path);
		File.Move(temp, path);
	}

	private static string DescribeFailure(HttpStatusCode status, string url, string what)
	{
		int num = (int)status;
		string text = Path.GetFileName(new Uri(url).AbsolutePath);
		if (string.IsNullOrEmpty(text))
		{
			text = "file";
		}
		if (num == 404)
		{
			return "The " + what + " file (" + text + ") is missing on the server.";
		}
		if (num == 401 || num == 403)
		{
			return "The server refused the " + what + " download (" + text + ", HTTP " + num + ").";
		}
		if (num >= 500)
		{
			return "The " + what + " server is having trouble (HTTP " + num + "). Try again later.";
		}
		return "Could not download " + text + " (HTTP " + num + ").";
	}

	private static string Sha256OfFile(string path)
	{
		using SHA256 sHA = SHA256.Create();
		using FileStream inputStream = File.OpenRead(path);
		byte[] array = sHA.ComputeHash(inputStream);
		StringBuilder stringBuilder = new StringBuilder(array.Length * 2);
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	private static void ExtractInto(string zipPath, string targetDir)
	{
		using ZipArchive zipArchive = ZipFile.OpenRead(zipPath);
		foreach (ZipArchiveEntry entry in zipArchive.Entries)
		{
			string fullPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
			if (!fullPath.StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
			{
				throw new Exception("Package contains a path outside the install folder: " + entry.FullName);
			}
			if (string.IsNullOrEmpty(entry.Name))
			{
				Directory.CreateDirectory(fullPath);
				continue;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
			entry.ExtractToFile(fullPath, overwrite: true);
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	public static void RegisterProtocol()
	{
		RegisterProtocolFor(Process.GetCurrentProcess().MainModule.FileName);
	}

	public static void RegisterProtocolFor(string exe)
	{
		string[] protocolSchemes = Config.ProtocolSchemes;
		for (int i = 0; i < protocolSchemes.Length; i++)
		{
			RegisterScheme(protocolSchemes[i], exe);
		}
	}

	private static void RegisterScheme(string scheme, string exe)
	{
		using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Classes\\" + scheme);
		registryKey.SetValue("", "URL:Kana Protocol");
		registryKey.SetValue("URL Protocol", "");
		using (RegistryKey registryKey2 = registryKey.CreateSubKey("DefaultIcon"))
		{
			registryKey2.SetValue("", exe + ",1");
		}
		using RegistryKey registryKey3 = registryKey.CreateSubKey("shell\\open\\command");
		registryKey3.SetValue("", "\"" + exe + "\" \"%1\"");
	}

	public static void CreateDesktopShortcut(string targetExe)
	{
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Kana.lnk");
			Type typeFromProgID = Type.GetTypeFromProgID("WScript.Shell");
			if (!(typeFromProgID == null))
			{
				object obj = Invoke(Activator.CreateInstance(typeFromProgID), "CreateShortcut", text);
				if (obj != null)
				{
					SetProperty(obj, "TargetPath", targetExe);
					SetProperty(obj, "WorkingDirectory", Path.GetDirectoryName(targetExe));
					SetProperty(obj, "IconLocation", targetExe + ",0");
					SetProperty(obj, "Description", "Kana");
					Invoke(obj, "Save");
				}
			}
		}
		catch
		{
		}
	}

	private static object Invoke(object target, string method, params object[] args)
	{
		return target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);
	}

	private static void SetProperty(object target, string property, object value)
	{
		target.GetType().InvokeMember(property, BindingFlags.SetProperty, null, target, new object[1] { value });
	}

	public async Task LaunchFromProtocolAsync(string argument, IProgress<Status> progress, CancellationToken ct)
	{
		if (string.IsNullOrEmpty(argument) || !HasProtocolScheme(argument))
		{
			return;
		}
		string url;
		try
		{
			url = ResolveJoinUrl(argument);
		}
		catch (Exception ex)
		{
			throw new JoinLinkException(ex.Message);
		}
		Log("launch argument: " + argument);
		Log("resolved url:    " + url);
		Uri parsed = new Uri(url);
		string ticket = ReadQueryValue(parsed.Query, "ticket");
		if (string.IsNullOrEmpty(ticket))
		{
			throw new JoinLinkException("The join link carries no ticket, so the game could not log in.");
		}
		int.TryParse(ReadQueryValue(parsed.Query, "placeId"), out var placeId);
		string player = PlayerPath();
		if (!File.Exists(player))
		{
			throw new Exception("The player is missing from the install. Run the installer again.");
		}
		ClientPatcher.EnsurePatched(Path.GetDirectoryName(player));
		int attempt = 1;
		while (true)
		{
			progress?.Report(new Status((attempt == 1) ? "Finding a game server..." : "Server unreachable, finding another one...", 90));
			PlaceLaunchResponse launch = await WaitForGameServerAsync(url, progress, ct).ConfigureAwait(continueOnCapturedContext: false);
			string value = (string.IsNullOrEmpty(launch.AuthenticationUrl) ? (parsed.Scheme + "://" + parsed.Authority + "/Login/Negotiate.ashx") : launch.AuthenticationUrl);
			string value2 = (string.IsNullOrEmpty(launch.AuthenticationTicket) ? ticket : launch.AuthenticationTicket);
			string text = url;
			if (text.IndexOf("ticket=", StringComparison.OrdinalIgnoreCase) < 0)
			{
				text = text + (text.Contains("?") ? "&" : "?") + "ticket=" + Uri.EscapeDataString(ticket);
			}
			Log("job:             " + (launch.JobId ?? "?"));
			Log("joinScriptUrl:   " + Clip(text, 300));
			Log("authentication:  " + Clip(value, 300));
			string text2 = "--authenticationUrl " + Quote(value) + " --authenticationTicket " + Quote(value2) + " --joinScriptUrl " + Quote(text);
			progress?.Report(new Status("Starting the game...", 100));
			Log("arguments:       " + text2);
			Process started = Process.Start(new ProcessStartInfo
			{
				FileName = player,
				Arguments = text2,
				WorkingDirectory = Path.GetDirectoryName(player),
				UseShellExecute = false
			});
			RenameGameWindow(started);
			if (!(await ClientCouldNotReachServerAsync(started, ct).ConfigureAwait(continueOnCapturedContext: false)))
			{
				KanaPresence.SpawnHelper(started, placeId, "2020");
				return;
			}
			Log("join failed: server " + (launch.JobId ?? "?") + " did not accept the connection");
			TryKill(started);
			if (attempt >= 3)
			{
				break;
			}
			await Task.Delay(1500, ct).ConfigureAwait(continueOnCapturedContext: false);
			attempt++;
		}
		throw new JoinLinkException("The game server did not accept the connection " + 3 + " times in a row.\nClick Play again in a minute. If it keeps failing, check your VPN or firewall.");
	}

	private static async Task<bool> ClientCouldNotReachServerAsync(Process client, CancellationToken ct)
	{
		string logDir = ClientLogDir();
		if (logDir == null)
		{
			return false;
		}
		DateTime since = DateTime.UtcNow.AddSeconds(-2.0);
		DateTime deadline = DateTime.UtcNow.AddSeconds(40.0);
		while (DateTime.UtcNow < deadline)
		{
			ct.ThrowIfCancellationRequested();
			string log = NewestClientLog(logDir, since);
			if (log != null)
			{
				string text = ReadShared(log);
				if (text != null)
				{
					if (text.IndexOf("Connection accepted", StringComparison.Ordinal) >= 0)
					{
						return false;
					}
					if (text.IndexOf("Failed to connect to ", StringComparison.Ordinal) >= 0)
					{
						await Task.Delay(2500, ct).ConfigureAwait(continueOnCapturedContext: false);
						return (ReadShared(log) ?? "").IndexOf("Connection accepted", StringComparison.Ordinal) < 0;
					}
				}
			}
			if (client.HasExited)
			{
				return false;
			}
			await Task.Delay(700, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		return false;
	}

	private static string ClientLogDir()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");
		if (Directory.Exists(text))
		{
			return text;
		}
		string text2 = Path.Combine(RootDir, "logs");
		if (!Directory.Exists(text2))
		{
			return null;
		}
		return text2;
	}

	private static string NewestClientLog(string dir, DateTime since)
	{
		try
		{
			return (from f in Directory.GetFiles(dir, "log_*.txt")
				select new FileInfo(f) into f
				where f.LastWriteTimeUtc >= since
				orderby f.LastWriteTimeUtc descending
				select f.FullName).FirstOrDefault();
		}
		catch
		{
			return null;
		}
	}

	private static string ReadShared(string path)
	{
		try
		{
			using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using StreamReader streamReader = new StreamReader(stream);
			return streamReader.ReadToEnd();
		}
		catch
		{
			return null;
		}
	}

	private static void TryKill(Process process)
	{
		try
		{
			if (process != null && !process.HasExited)
			{
				process.Kill();
			}
		}
		catch
		{
		}
	}

	private async Task<PlaceLaunchResponse> WaitForGameServerAsync(string url, IProgress<Status> progress, CancellationToken ct)
	{
		DateTime deadline = DateTime.UtcNow + ServerStartTimeout;
		string lastError = null;
		PlaceLaunchResponse response;
		while (true)
		{
			ct.ThrowIfCancellationRequested();
			response = null;
			try
			{
				response = await FetchPlaceLauncherAsync(url, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex2)
			{
				lastError = ex2.Message;
				Log("placelauncher failed: " + lastError);
			}
			if (response != null)
			{
				if (response.Status == 2)
				{
					return response;
				}
				if (response.Status != 1 && response.Status != 3)
				{
					throw new JoinLinkException(response.Message ?? "No game server is available for this place.");
				}
				progress?.Report(new Status(response.Message ?? "Starting the game server...", 95));
			}
			if (DateTime.UtcNow >= deadline)
			{
				break;
			}
			await Task.Delay(1500, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (response != null)
		{
			throw new JoinLinkException("The game server did not start in time. Click Play again on the website.");
		}
		throw new JoinLinkException("Could not reach the game server." + (string.IsNullOrEmpty(lastError) ? "" : (" (" + Clip(lastError, 120) + ")")));
	}

	private static string Clip(string value, int max)
	{
		if (!string.IsNullOrEmpty(value) && value.Length > max)
		{
			return value.Substring(0, max);
		}
		return value;
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool SetWindowText(IntPtr hWnd, string text);

	private static void RenameGameWindow(Process game)
	{
		if (game == null)
		{
			return;
		}
		Thread thread = new Thread((ThreadStart)delegate
		{
			try
			{
				int num = 0;
				for (int i = 0; i < 90; i++)
				{
					if (num >= 20)
					{
						break;
					}
					Thread.Sleep(1000);
					if (game.HasExited)
					{
						break;
					}
					game.Refresh();
					IntPtr mainWindowHandle = game.MainWindowHandle;
					if (!(mainWindowHandle == IntPtr.Zero))
					{
						SetWindowText(mainWindowHandle, "Kana");
						num++;
					}
				}
			}
			catch
			{
			}
		});
		thread.IsBackground = false;
		thread.Start();
	}

	private static string Quote(string value)
	{
		return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
	}

	private static string ReadQueryValue(string query, string key)
	{
		string[] array = (query ?? string.Empty).TrimStart('?').Split('&');
		foreach (string text in array)
		{
			int num = text.IndexOf('=');
			if (num > 0 && string.Equals(text.Substring(0, num), key, StringComparison.OrdinalIgnoreCase))
			{
				return Uri.UnescapeDataString(text.Substring(num + 1).Replace("+", " "));
			}
		}
		return null;
	}

	private async Task<PlaceLaunchResponse> FetchPlaceLauncherAsync(string url, CancellationToken ct)
	{
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.TryAddWithoutValidation("User-Agent", "Roblox/WinInet");
		using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(continueOnCapturedContext: false);
		response.EnsureSuccessStatusCode();
		string text = await response.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
		Log("placelauncher:   " + ((text.Length > 200) ? (text.Substring(0, 200) + "...") : text));
		return Deserialize<PlaceLaunchResponse>(text);
	}

	internal static bool HasProtocolScheme(string argument)
	{
		return Config.ProtocolSchemes.Any((string s) => argument.StartsWith(s + ":", StringComparison.OrdinalIgnoreCase));
	}

	internal static string ResolveJoinUrl(string argument)
	{
		string text = (Config.ProtocolSchemes.FirstOrDefault((string s) => argument.StartsWith(s + ":", StringComparison.OrdinalIgnoreCase)) ?? Config.ProtocolSchemes[0]) + ":";
		string text2 = argument.Substring(text.Length).TrimStart('/');
		if (!text2.StartsWith("http", StringComparison.OrdinalIgnoreCase))
		{
			string[] array = text2.Split('+');
			foreach (string text3 in array)
			{
				int num2 = text3.IndexOf(':');
				if (num2 > 0 && string.Equals(text3.Substring(0, num2), "placelauncherurl", StringComparison.OrdinalIgnoreCase))
				{
					string text4 = text3.Substring(num2 + 1);
					if (string.IsNullOrEmpty(text4))
					{
						break;
					}
					text4 = Uri.UnescapeDataString(text4);
					text4 = Regex.Replace(text4, "^(https?):?/{1,3}", "$1://", RegexOptions.IgnoreCase);
					if (!Uri.TryCreate(text4, UriKind.Absolute, out var result) || (!(result.Scheme == Uri.UriSchemeHttp) && !(result.Scheme == Uri.UriSchemeHttps)))
					{
						break;
					}
					return text4;
				}
			}
		}
		string text5 = text2;
		if (Regex.IsMatch(text5, "^https?%3A", RegexOptions.IgnoreCase))
		{
			text5 = Uri.UnescapeDataString(text5);
		}
		text5 = Regex.Replace(text5, "^(https?):?/{1,3}", "$1://", RegexOptions.IgnoreCase);
		if (!Uri.TryCreate(text5, UriKind.Absolute, out var result2) || (result2.Scheme != Uri.UriSchemeHttp && result2.Scheme != Uri.UriSchemeHttps))
		{
			Log("could not read launch argument: " + argument);
			throw new Exception("Could not read the join link from the website.");
		}
		return text5;
	}

	internal static void Log(string line)
	{
		try
		{
			Directory.CreateDirectory(RootDir);
			File.AppendAllText(Path.Combine(RootDir, "launch.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
		}
		catch
		{
		}
	}

	private static T Deserialize<T>(string json) where T : class
	{
		using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty));
		return new DataContractJsonSerializer(typeof(T)).ReadObject((Stream)stream) as T;
	}
}
