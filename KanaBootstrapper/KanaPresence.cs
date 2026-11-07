using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace KanaBootstrapper;

internal static class KanaPresence
{
	public static bool Enabled => !string.IsNullOrWhiteSpace("1566460284615266325");

	public static DiscordActivity BuildActivity(int placeId, string era, GameInfo info, DateTime? startedAt = null)
	{
		string text = (string.IsNullOrWhiteSpace(era) ? "client" : (era + " client"));
		DiscordActivity discordActivity = new DiscordActivity
		{
			StartedAt = (startedAt ?? DateTime.UtcNow),
			Details = ((info != null && !string.IsNullOrEmpty(info.Name)) ? info.Name : "Playing on Kana"),
			State = ((info != null && !string.IsNullOrEmpty(info.Creator)) ? ("by " + info.Creator + " · " + text) : text)
		};
		string text2 = info?.ThumbnailUrl;
		if (!string.IsNullOrEmpty(text2))
		{
			discordActivity.LargeImage = text2;
			discordActivity.LargeText = ((info != null && !string.IsNullOrEmpty(info.Name)) ? info.Name : "Kana");
			discordActivity.SmallImage = "k-27-06-20226";
			discordActivity.SmallText = "Kana";
		}
		else
		{
			discordActivity.LargeImage = "k-27-06-20226";
			discordActivity.LargeText = "Kana";
		}
		string url = ((info != null && !string.IsNullOrEmpty(info.PageUrl)) ? info.PageUrl : ((placeId > 0) ? GameInfo.BuildPageUrl(placeId, null) : "https://kanade.cfd"));
		discordActivity.Buttons.Add(new DiscordButton
		{
			Label = "View game",
			Url = url
		});
		discordActivity.Buttons.Add(new DiscordButton
		{
			Label = "Get Kana",
			Url = "https://kanade.cfd"
		});
		return discordActivity;
	}

	private static void LogActivity(DiscordActivity activity)
	{
		Installer.Log("discord: card: " + activity.Details + " | " + activity.State + " | large: " + (activity.LargeImage ?? "-") + " | small: " + (activity.SmallImage ?? "-") + " | buttons: " + string.Join(", ", activity.Buttons.ConvertAll((DiscordButton b) => b.Label)));
	}

	private static int CountPlayers()
	{
		try
		{
			return Process.GetProcessesByName(Path.GetFileNameWithoutExtension("KanaPlayerBeta.exe")).Length;
		}
		catch
		{
			return 0;
		}
	}

	public static void Run(int gamePid, int placeId, string era)
	{
		if (!Enabled)
		{
			return;
		}
		Process processById;
		try
		{
			processById = Process.GetProcessById(gamePid);
		}
		catch
		{
			return;
		}
		using DiscordRpc discordRpc = new DiscordRpc("1566460284615266325");
		DateTime utcNow = DateTime.UtcNow;
		DiscordActivity activity = BuildActivity(placeId, era, null, utcNow);
		LogActivity(activity);
		discordRpc.Update(activity);
		discordRpc.Start();
		Installer.Log("discord: helper attached to pid " + gamePid + " (place " + placeId + ")");
		if (placeId > 0)
		{
			GameInfo gameInfo = GameInfo.TryFetch(placeId);
			if (gameInfo != null)
			{
				DiscordActivity activity2 = BuildActivity(placeId, era, gameInfo, utcNow);
				LogActivity(activity2);
				discordRpc.Update(activity2);
			}
		}
		try
		{
			processById.WaitForExit();
		}
		catch
		{
		}
		int num = 0;
		while (num < 2)
		{
			num = ((CountPlayers() <= 0) ? (num + 1) : 0);
			if (num < 2)
			{
				Thread.Sleep(2000);
			}
		}
		Installer.Log("discord: game " + gamePid + " exited");
	}

	public static void SpawnHelper(Process game, int placeId, string era)
	{
		if (!Enabled || game == null)
		{
			return;
		}
		try
		{
			string fileName = Process.GetCurrentProcess().MainModule.FileName;
			ProcessStartInfo processStartInfo = new ProcessStartInfo();
			processStartInfo.FileName = fileName;
			processStartInfo.Arguments = "--rpc-attach " + game.Id + " " + placeId + " " + (era ?? "2020");
			processStartInfo.WorkingDirectory = Path.GetDirectoryName(fileName) ?? string.Empty;
			processStartInfo.UseShellExecute = false;
			processStartInfo.CreateNoWindow = true;
			processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
			Process.Start(processStartInfo);
			Installer.Log("discord: helper started for pid " + game.Id + ", place " + placeId);
		}
		catch (Exception ex)
		{
			Installer.Log("discord: could not start helper: " + ex.Message);
		}
	}
}
