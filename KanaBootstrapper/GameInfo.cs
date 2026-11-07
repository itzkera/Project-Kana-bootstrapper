using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace KanaBootstrapper;

internal sealed class GameInfo
{
	[DataContract]
	private sealed class PlaceDetails
	{
		[DataMember(Name = "placeId")]
		public int PlaceId { get; set; }

		[DataMember(Name = "name")]
		public string Name { get; set; }

		[DataMember(Name = "builder")]
		public string Builder { get; set; }

		[DataMember(Name = "universeId")]
		public int UniverseId { get; set; }

		[DataMember(Name = "playerCount")]
		public int PlayerCount { get; set; }

		[DataMember(Name = "maxPlayerCount")]
		public int MaxPlayerCount { get; set; }
	}

	[DataContract]
	private sealed class IconResponse
	{
		[DataMember(Name = "data")]
		public List<IconEntry> Data { get; set; }
	}

	[DataContract]
	private sealed class IconEntry
	{
		[DataMember(Name = "imageUrl")]
		public string ImageUrl { get; set; }
	}

	public int PlaceId;

	public string Name;

	public string Creator;

	public int UniverseId;

	public int PlayerCount;

	public int MaxPlayers;

	public string PageUrl;

	public string ThumbnailUrl;

	public static GameInfo TryFetch(int placeId, int timeoutMs = 5000)
	{
		if (placeId <= 0)
		{
			return null;
		}
		try
		{
			using HttpClient httpClient = new HttpClient
			{
				Timeout = TimeSpan.FromMilliseconds(timeoutMs)
			};
			httpClient.DefaultRequestHeaders.Add("User-Agent", "KanaBootstrapper/1.0");
			string requestUri = "https://kanade.cfd/apisite/games/v1/games/multiget-place-details?placeIds=" + placeId;
			List<PlaceDetails> list = Deserialize<List<PlaceDetails>>(httpClient.GetStringAsync(requestUri).GetAwaiter().GetResult());
			PlaceDetails placeDetails = ((list != null && list.Count > 0) ? list[0] : null);
			if (placeDetails == null)
			{
				return null;
			}
			return new GameInfo
			{
				PlaceId = ((placeDetails.PlaceId > 0) ? placeDetails.PlaceId : placeId),
				Name = (string.IsNullOrWhiteSpace(placeDetails.Name) ? null : placeDetails.Name.Trim()),
				Creator = (string.IsNullOrWhiteSpace(placeDetails.Builder) ? null : placeDetails.Builder.Trim()),
				UniverseId = placeDetails.UniverseId,
				PlayerCount = placeDetails.PlayerCount,
				MaxPlayers = placeDetails.MaxPlayerCount,
				PageUrl = BuildPageUrl(placeId, placeDetails.Name),
				ThumbnailUrl = FetchIconUrl(httpClient, placeDetails.UniverseId)
			};
		}
		catch (Exception ex)
		{
			Installer.Log("discord: place " + placeId + " lookup failed: " + ex.Message);
			return null;
		}
	}

	private static string FetchIconUrl(HttpClient http, int universeId)
	{
		if (universeId <= 0)
		{
			return null;
		}
		try
		{
			string requestUri = "https://kanade.cfd/apisite/thumbnails/v1/games/icons?universeIds=" + universeId;
			return (Deserialize<IconResponse>(http.GetStringAsync(requestUri).GetAwaiter().GetResult())?.Data?.FirstOrDefault((IconEntry d) => !string.IsNullOrWhiteSpace(d.ImageUrl)))?.ImageUrl.Trim();
		}
		catch (Exception ex)
		{
			Installer.Log("discord: icon lookup for universe " + universeId + " failed: " + ex.Message);
			return null;
		}
	}

	public static string BuildPageUrl(int placeId, string name)
	{
		string text = "https://kanade.cfd/games/" + placeId;
		string text2 = Slug(name);
		if (text2.Length <= 0)
		{
			return text;
		}
		return text + "/" + text2;
	}

	private static string Slug(string value)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string text = (value ?? string.Empty).ToLowerInvariant();
		foreach (char c in text)
		{
			if (char.IsLetterOrDigit(c))
			{
				stringBuilder.Append(c);
			}
			else if (stringBuilder.Length > 0 && stringBuilder[stringBuilder.Length - 1] != '-')
			{
				stringBuilder.Append('-');
			}
		}
		return stringBuilder.ToString().Trim('-');
	}

	private static T Deserialize<T>(string json) where T : class
	{
		using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty));
		return new DataContractJsonSerializer(typeof(T)).ReadObject((Stream)stream) as T;
	}
}
