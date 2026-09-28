using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ContextShell.UI
{
	public sealed class ReleaseInfo
	{
		public Version Version;
		public string Tag;
		public string Name;
		public string Notes;
		public string PageUrl;
		public string SetupUrl;
		public string SetupName;
		public string ChecksumsUrl;
		public DateTime Published;
	}

	/// <summary>
	/// Checks GitHub Releases for a newer ContextShell and downloads its setup EXE.
	/// Downloads are verified against the release's SHA256SUMS.txt before they run.
	/// </summary>
	public static class Updates
	{
		private static readonly HttpClient Http = CreateClient();

		private static HttpClient CreateClient()
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
			client.DefaultRequestHeaders.UserAgent.ParseAdd($"ContextShell/{AppInfo.VersionText} (+{AppInfo.RepositoryUrl})");
			client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
			return client;
		}

		public static async Task<ReleaseInfo> GetLatestAsync(CancellationToken ct = default)
		{
			var url = $"https://api.github.com/repos/{AppInfo.Repository}/releases/latest";
			using(var response = await Http.GetAsync(url, ct).ConfigureAwait(false))
			{
				if(response.StatusCode == HttpStatusCode.NotFound)
					return null; // no releases yet
				response.EnsureSuccessStatusCode();
				var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
				return Parse(json);
			}
		}

		[DataContract]
		private sealed class GhAsset
		{
			[DataMember(Name = "name")] public string Name;
			[DataMember(Name = "browser_download_url")] public string Url;
		}

		[DataContract]
		private sealed class GhRelease
		{
			[DataMember(Name = "tag_name")] public string Tag;
			[DataMember(Name = "name")] public string Name;
			[DataMember(Name = "body")] public string Body;
			[DataMember(Name = "html_url")] public string HtmlUrl;
			[DataMember(Name = "published_at")] public string PublishedAt;
			[DataMember(Name = "assets")] public GhAsset[] Assets;
		}

		internal static ReleaseInfo Parse(string json)
		{
			GhRelease gh;
			using(var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
				gh = (GhRelease)new DataContractJsonSerializer(typeof(GhRelease)).ReadObject(ms);

			var version = ParseVersion(gh?.Tag);
			if(version == null)
				return null;

			var release = new ReleaseInfo
			{
				Version = version,
				Tag = gh.Tag,
				Name = string.IsNullOrEmpty(gh.Name) ? gh.Tag : gh.Name,
				Notes = gh.Body ?? "",
				PageUrl = gh.HtmlUrl ?? AppInfo.RepositoryUrl + "/releases",
			};
			if(DateTime.TryParse(gh.PublishedAt, out var published))
				release.Published = published;

			var arch = AppInfo.OSArchitecture;
			foreach(var asset in gh.Assets ?? new GhAsset[0])
			{
				if(asset?.Name == null || asset.Url == null)
					continue;
				if(asset.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
					release.ChecksumsUrl = asset.Url;
				else if(asset.Name.EndsWith($"-{arch}-setup.exe", StringComparison.OrdinalIgnoreCase))
				{
					release.SetupUrl = asset.Url;
					release.SetupName = asset.Name;
				}
			}
			return release;
		}

		public static Version ParseVersion(string tag)
		{
			if(string.IsNullOrWhiteSpace(tag))
				return null;
			var m = Regex.Match(tag, @"(\d+)\.(\d+)(?:\.(\d+))?");
			if(!m.Success)
				return null;
			return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
				m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
		}

		public static bool IsNewer(ReleaseInfo release) =>
			release != null && release.Version > AppInfo.Version;

		/// <summary>Download the setup EXE to %TEMP% and verify its SHA-256. Returns the file path.</summary>
		public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken ct)
		{
			if(release.SetupUrl == null)
				throw new InvalidOperationException($"This release has no installer for {AppInfo.OSArchitecture} Windows.");
			if(release.ChecksumsUrl == null)
				throw new InvalidOperationException("This release has no SHA256SUMS.txt, so the download can't be verified.");

			var sums = await Http.GetStringAsync(release.ChecksumsUrl).ConfigureAwait(false);
			var expected = sums.Split('\n')
				.Select(l => l.Trim().Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries))
				.Where(parts => parts.Length == 2 && parts[1].Equals(release.SetupName, StringComparison.OrdinalIgnoreCase))
				.Select(parts => parts[0].ToLowerInvariant())
				.FirstOrDefault();
			if(expected == null)
				throw new InvalidOperationException($"SHA256SUMS.txt has no entry for {release.SetupName}.");

			var dir = Path.Combine(Path.GetTempPath(), "ContextShell-Update");
			Directory.CreateDirectory(dir);
			var path = Path.Combine(dir, release.SetupName);

			using(var response = await Http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
			{
				response.EnsureSuccessStatusCode();
				var total = response.Content.Headers.ContentLength ?? -1;
				using(var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
				using(var output = File.Create(path))
				using(var sha = SHA256.Create())
				{
					var buffer = new byte[81920];
					long read = 0;
					int n;
					while((n = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
					{
						await output.WriteAsync(buffer, 0, n, ct).ConfigureAwait(false);
						sha.TransformBlock(buffer, 0, n, null, 0);
						read += n;
						if(total > 0)
							progress?.Report((double)read / total);
					}
					sha.TransformFinalBlock(buffer, 0, 0);
					var actual = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
					if(actual != expected)
					{
						output.Close();
						File.Delete(path);
						throw new InvalidOperationException("The downloaded installer failed its checksum check and was deleted.");
					}
				}
			}
			return path;
		}

		// Per-user preferences

		public static bool AutoCheckEnabled
		{
			get => ReadInt("AutoCheck", 1) != 0;
			set => WriteInt("AutoCheck", value ? 1 : 0);
		}

		public static string SkippedVersion
		{
			get => ReadString("SkippedVersion");
			set => WriteString("SkippedVersion", value ?? "");
		}

		public static DateTime LastCheck
		{
			get => DateTime.TryParse(ReadString("LastCheck"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue;
			set => WriteString("LastCheck", value.ToString("o"));
		}

		private static int ReadInt(string name, int fallback)
		{
			try
			{
				using(var key = Registry.CurrentUser.OpenSubKey(AppInfo.UserKey))
					return key?.GetValue(name) is int v ? v : fallback;
			}
			catch
			{
				return fallback;
			}
		}

		private static string ReadString(string name)
		{
			try
			{
				using(var key = Registry.CurrentUser.OpenSubKey(AppInfo.UserKey))
					return key?.GetValue(name) as string;
			}
			catch
			{
				return null;
			}
		}

		private static void WriteInt(string name, int value)
		{
			try
			{
				using(var key = Registry.CurrentUser.CreateSubKey(AppInfo.UserKey))
					key.SetValue(name, value, RegistryValueKind.DWord);
			}
			catch
			{
			}
		}

		private static void WriteString(string name, string value)
		{
			try
			{
				using(var key = Registry.CurrentUser.CreateSubKey(AppInfo.UserKey))
					key.SetValue(name, value);
			}
			catch
			{
			}
		}
	}
}
