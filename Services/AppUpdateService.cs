using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using TarkovTracker.Models;

namespace TarkovTracker.Services;

public sealed class AppUpdateCheckResult
{
    public bool Succeeded { get; init; }
    public bool UpdateAvailable { get; init; }
    public string CurrentVersion { get; init; } = "";
    public string LatestVersion { get; init; } = "";
    public string ReleaseName { get; init; } = "";
    public string ReleaseUrl { get; init; } = "";
    public string Message { get; init; } = "";
}

public static class AppUpdateService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SayserTarkovTracker", AppInfo.InterfaceVersion));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<AppUpdateCheckResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        string currentLabel = AppInfo.VersionLabel;
        if (!TryParseVersion(currentLabel, out Version currentVersion))
            currentVersion = new Version(0, 0, 0);

        try
        {
            using HttpResponseMessage response = await Http.GetAsync(
                AppInfo.GitHubLatestReleaseApiUrl,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new AppUpdateCheckResult
                {
                    Succeeded = false,
                    CurrentVersion = currentLabel,
                    Message = $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}."
                };
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            GitHubRelease? release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);

            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new AppUpdateCheckResult
                {
                    Succeeded = false,
                    CurrentVersion = currentLabel,
                    Message = "Could not read the latest release from GitHub."
                };
            }

            string latestLabel = NormalizeTag(release.TagName);
            if (!TryParseVersion(latestLabel, out Version latestVersion))
            {
                return new AppUpdateCheckResult
                {
                    Succeeded = false,
                    CurrentVersion = currentLabel,
                    LatestVersion = latestLabel,
                    ReleaseName = release.Name ?? "",
                    ReleaseUrl = release.HtmlUrl ?? AppInfo.GitHubReleasesPageUrl,
                    Message = $"Latest release tag '{release.TagName}' is not a recognizable version."
                };
            }

            bool updateAvailable = latestVersion > currentVersion;
            return new AppUpdateCheckResult
            {
                Succeeded = true,
                UpdateAvailable = updateAvailable,
                CurrentVersion = currentLabel,
                LatestVersion = latestLabel,
                ReleaseName = string.IsNullOrWhiteSpace(release.Name) ? latestLabel : release.Name!,
                ReleaseUrl = string.IsNullOrWhiteSpace(release.HtmlUrl)
                    ? AppInfo.GitHubReleasesPageUrl
                    : release.HtmlUrl!,
                Message = updateAvailable
                    ? $"Update available: {latestLabel} (you have {currentLabel})."
                    : $"You are up to date ({currentLabel})."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AppUpdateCheckResult
            {
                Succeeded = false,
                CurrentVersion = currentLabel,
                Message = $"Update check failed: {ex.Message}"
            };
        }
    }

    public static void OpenReleasePage(string? url)
    {
        string target = string.IsNullOrWhiteSpace(url) ? AppInfo.GitHubReleasesPageUrl : url!;
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private static string NormalizeTag(string tag)
    {
        tag = tag.Trim();
        if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            tag = tag[1..];
        return tag;
    }

    private static bool TryParseVersion(string label, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(label))
            return false;

        string cleaned = NormalizeTag(label);
        // Accept 2.7.3 or 2.7
        string[] parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        try
        {
            int major = int.Parse(parts[0]);
            int minor = parts.Length > 1 ? int.Parse(parts[1]) : 0;
            int build = parts.Length > 2 ? int.Parse(parts[2]) : 0;
            version = new Version(major, minor, build);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}
