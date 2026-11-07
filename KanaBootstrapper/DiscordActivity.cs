using System;
using System.Collections.Generic;

namespace KanaBootstrapper;

internal sealed class DiscordActivity
{
	public string Details;

	public string State;

	public string LargeImage;

	public string LargeText;

	public string SmallImage;

	public string SmallText;

	public DateTime StartedAt = DateTime.UtcNow;

	public readonly List<DiscordButton> Buttons = new List<DiscordButton>();
}
