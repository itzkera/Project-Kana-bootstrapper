using System;
using System.Linq;
using System.Windows.Forms;

namespace KanaBootstrapper;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		if (TryRunPresenceHelper(args))
		{
			return;
		}
		if (args.Any((string a) => a.Equals("--rpc", StringComparison.OrdinalIgnoreCase)))
		{
			RunPresencePreview();
			return;
		}
		string protocolArgument = args.FirstOrDefault(Installer.HasProtocolScheme);
		bool preview = args.Any((string a) => a.Equals("--preview", StringComparison.OrdinalIgnoreCase));
		Application.Run(new MainForm(protocolArgument)
		{
			Preview = preview
		});
	}

	private static bool TryRunPresenceHelper(string[] args)
	{
		int num = Array.FindIndex(args, (string a) => a.Equals("--rpc-attach", StringComparison.OrdinalIgnoreCase));
		if (num < 0)
		{
			return false;
		}
		int result = 0;
		int result2 = 0;
		if (num + 1 < args.Length)
		{
			int.TryParse(args[num + 1], out result);
		}
		if (num + 2 < args.Length)
		{
			int.TryParse(args[num + 2], out result2);
		}
		string era = ((num + 3 < args.Length && !string.IsNullOrWhiteSpace(args[num + 3])) ? args[num + 3] : "2020");
		if (result > 0)
		{
			KanaPresence.Run(result, result2, era);
		}
		return true;
	}

	private static void RunPresencePreview()
	{
		if (!Config.DiscordEnabled)
		{
			MessageBox.Show("Set Config.DiscordClientId first (Discord Developer Portal, Application ID).", "Kana", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		GameInfo info = GameInfo.TryFetch(1, 8000);
		DiscordActivity activity = KanaPresence.BuildActivity(1, "2020", info);
		DiscordRpc discordRpc = new DiscordRpc("1566460284615266325");
		discordRpc.Update(activity);
		discordRpc.Start();
		MessageBox.Show("Presence is live. Close this window to stop.", "Kana", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		discordRpc.Dispose();
	}
}
