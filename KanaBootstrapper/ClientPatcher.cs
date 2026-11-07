using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace KanaBootstrapper;

internal static class ClientPatcher
{
	private const string PatchMarker = ".kana-patched-v2";

	private const string DomainFrom = "roblox.com";

	private const string DomainTo = "kanade.cfd";

	private static readonly byte[] Rsa2Header = new byte[12]
	{
		6, 2, 0, 0, 0, 164, 0, 0, 82, 83,
		65, 50
	};

	public static void EnsurePatched(string clientDir)
	{
		if (string.IsNullOrWhiteSpace(clientDir) || !Directory.Exists(clientDir))
		{
			return;
		}
		string path = Path.Combine(clientDir, ".kana-patched-v2");
		if (File.Exists(path))
		{
			return;
		}
		List<string> list = (from p in Directory.GetFiles(clientDir, "*.*", SearchOption.AllDirectories)
			where p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
			select p).ToList();
		if (list.Count == 0)
		{
			return;
		}
		byte[] array = LoadPublicKeyBlob();
		int num = 0;
		int num2 = 0;
		foreach (string item in list)
		{
			byte[] data = File.ReadAllBytes(item);
			bool flag = false;
			if (PatchDomain(ref data))
			{
				num++;
				flag = true;
			}
			if (array != null && PatchRsa2Key(ref data, array))
			{
				num2++;
				flag = true;
			}
			if (flag)
			{
				File.WriteAllBytes(item, data);
			}
		}
		if (num2 > 0)
		{
			File.WriteAllText(path, DateTime.UtcNow.ToString("o"));
		}
	}

	private static byte[] LoadPublicKeyBlob()
	{
		try
		{
			using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("KanaBootstrapper.PublicKeyBlob2048.bin");
			if (stream == null)
			{
				return null;
			}
			byte[] array = new byte[stream.Length];
			stream.Read(array, 0, array.Length);
			return array;
		}
		catch
		{
			return null;
		}
	}

	private static bool PatchDomain(ref byte[] data)
	{
		byte[] bytes = Encoding.ASCII.GetBytes("roblox.com");
		byte[] bytes2 = Encoding.ASCII.GetBytes("kanade.cfd");
		if (bytes2.Length != bytes.Length)
		{
			return false;
		}
		int num = 0;
		for (int i = 0; i <= data.Length - bytes.Length; i++)
		{
			bool flag = true;
			for (int j = 0; j < bytes.Length; j++)
			{
				if (data[i + j] != bytes[j])
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				for (int k = 0; k < bytes2.Length; k++)
				{
					data[i + k] = bytes2[k];
				}
				num++;
			}
		}
		return num > 0;
	}

	private static bool PatchRsa2Key(ref byte[] data, byte[] newKey)
	{
		for (int i = 0; i <= data.Length - Rsa2Header.Length; i++)
		{
			bool flag = true;
			for (int j = 0; j < Rsa2Header.Length; j++)
			{
				if (data[i + j] != Rsa2Header[j])
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				uint num = BitConverter.ToUInt32(data, i + 12);
				if (num == 2048 && 20 + num / 8 == (uint)newKey.Length)
				{
					Buffer.BlockCopy(newKey, 0, data, i, newKey.Length);
					return true;
				}
			}
		}
		return false;
	}
}
