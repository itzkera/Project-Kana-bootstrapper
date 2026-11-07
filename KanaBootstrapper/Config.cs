namespace KanaBootstrapper;

internal static class Config
{
	public const string ProductName = "Kana";

	public const string DisplayName = "Kana";

	public const string SiteUrl = "https://kanade.cfd";

	public static readonly ClientSource Source = ClientSource.Archive;

	public const string ClientArchiveUrl = "https://cdn.kanade.cfd/2020L.zip";

	public const string ClientEra = "2020";

	public const string ManifestUrl = "https://kanade.cfd/client/manifest-v2.json";

	public const string PlayerExecutable = "KanaPlayerBeta.exe";

	public static readonly string[] ProtocolSchemes = new string[3] { "kana-player", "kana-player-2020", "kana-player-2021" };

	public const int ParallelDownloads = 3;

	public const string DiscordClientId = "1566460284615266325";

	public const string DiscordLogoImage = "k-27-06-20226";

	public const string DiscordLogoText = "Kana";

	public const string DiscordPlayButtonLabel = "View game";

	public const string DiscordGetButtonLabel = "Get Kana";

	public const string DiscordGetButtonUrl = "https://kanade.cfd";

	public static bool DiscordEnabled => !string.IsNullOrWhiteSpace("1566460284615266325");
}
