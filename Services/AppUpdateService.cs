using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
    public string? DownloadUrl { get; init; }
    public string? DownloadFileName { get; init; }
    public string Message { get; init; } = "";
}

public sealed class AppUpdateDownloadResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = "";
    public bool RestartScheduled { get; init; }
}

public static class AppUpdateService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
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

            GitHubAsset? asset = PickDownloadAsset(release.Assets);
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
                DownloadUrl = asset?.BrowserDownloadUrl,
                DownloadFileName = asset?.Name,
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

    public static async Task<AppUpdateDownloadResult> DownloadAndInstallAsync(
        AppUpdateCheckResult check,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!check.UpdateAvailable)
        {
            return new AppUpdateDownloadResult
            {
                Succeeded = false,
                Message = "No update is available to download."
            };
        }

        if (string.IsNullOrWhiteSpace(check.DownloadUrl))
        {
            return new AppUpdateDownloadResult
            {
                Succeeded = false,
                Message = "The latest release has no downloadable .zip or .exe asset."
            };
        }

        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
        {
            return new AppUpdateDownloadResult
            {
                Succeeded = false,
                Message = "Could not determine the running application path."
            };
        }

        string? exeDir = Path.GetDirectoryName(currentExe);
        if (string.IsNullOrWhiteSpace(exeDir))
        {
            return new AppUpdateDownloadResult
            {
                Succeeded = false,
                Message = "Could not determine the application folder."
            };
        }

        string workRoot = Path.Combine(Path.GetTempPath(), "SayserTarkovTracker-update");
        string workDir = Path.Combine(workRoot, check.LatestVersion + "-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(workDir);
            string fileName = string.IsNullOrWhiteSpace(check.DownloadFileName)
                ? "update.bin"
                : Path.GetFileName(check.DownloadFileName);
            string downloadPath = Path.Combine(workDir, fileName);

            progress?.Report("Downloading update…");
            await DownloadFileAsync(check.DownloadUrl!, downloadPath, cancellationToken);

            progress?.Report("Preparing install…");
            string newExePath = ResolveNewExePath(downloadPath, workDir);
            if (string.IsNullOrWhiteSpace(newExePath) || !File.Exists(newExePath))
            {
                return new AppUpdateDownloadResult
                {
                    Succeeded = false,
                    Message = "Downloaded package did not contain TarkovTracker.exe."
                };
            }

            string targetExe = Path.Combine(exeDir, "TarkovTracker.exe");
            // Prefer replacing the running file path when it already is TarkovTracker.exe;
            // otherwise install beside the current process as TarkovTracker.exe.
            if (string.Equals(Path.GetFileName(currentExe), "TarkovTracker.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(currentExe), "SayserTarkovTracker.exe", StringComparison.OrdinalIgnoreCase))
            {
                targetExe = currentExe;
            }

            string backupExe = targetExe + ".bak";
            string scriptPath = Path.Combine(workDir, "apply-update.cmd");
            WriteApplyUpdateScript(
                scriptPath,
                currentPid: Environment.ProcessId,
                sourceExe: newExePath,
                targetExe: targetExe,
                backupExe: backupExe,
                workDir: workDir);

            progress?.Report("Restarting into the new version…");
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{scriptPath}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workDir
            });

            return new AppUpdateDownloadResult
            {
                Succeeded = true,
                RestartScheduled = true,
                Message = $"Downloaded {check.LatestVersion}. The app will restart to finish installing."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            try
            {
                if (Directory.Exists(workDir))
                    Directory.Delete(workDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            return new AppUpdateDownloadResult
            {
                Succeeded = false,
                Message = $"Download failed: {ex.Message}"
            };
        }
    }

    public static void OpenReleasePage(string? url)
    {
        string target = string.IsNullOrWhiteSpace(url) ? AppInfo.GitHubReleasesPageUrl : url!;
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue("SayserTarkovTracker", AppInfo.InterfaceVersion));

        using HttpResponseMessage response = await Http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream destination = new(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static string ResolveNewExePath(string downloadPath, string workDir)
    {
        string ext = Path.GetExtension(downloadPath);
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return downloadPath;

        if (!ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return "";

        string extractDir = Path.Combine(workDir, "extracted");
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(downloadPath, extractDir, overwriteFiles: true);

        string[] candidates = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories);
        string? preferred = candidates.FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), "TarkovTracker.exe", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(preferred))
            return preferred;

        preferred = candidates.FirstOrDefault(path =>
            Path.GetFileName(path).Contains("TarkovTracker", StringComparison.OrdinalIgnoreCase));
        return preferred ?? "";
    }

    private static void WriteApplyUpdateScript(
        string scriptPath,
        int currentPid,
        string sourceExe,
        string targetExe,
        string backupExe,
        string workDir)
    {
        // Wait for this process to exit, swap the exe, relaunch, then clean up.
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal");
        sb.AppendLine($":waitloop");
        sb.AppendLine($"tasklist /FI \"PID eq {currentPid}\" 2>NUL | find \"{currentPid}\" >NUL");
        sb.AppendLine("if not errorlevel 1 (");
        sb.AppendLine("  timeout /t 1 /nobreak >NUL");
        sb.AppendLine("  goto waitloop");
        sb.AppendLine(")");
        sb.AppendLine($"if exist \"{backupExe}\" del /f /q \"{backupExe}\"");
        sb.AppendLine($"if exist \"{targetExe}\" move /y \"{targetExe}\" \"{backupExe}\" >NUL");
        sb.AppendLine($"copy /y \"{sourceExe}\" \"{targetExe}\" >NUL");
        sb.AppendLine($"start \"\" \"{targetExe}\"");
        sb.AppendLine($"timeout /t 2 /nobreak >NUL");
        sb.AppendLine($"if exist \"{backupExe}\" del /f /q \"{backupExe}\"");
        sb.AppendLine($"rd /s /q \"{workDir}\" >NUL 2>&1");
        File.WriteAllText(scriptPath, sb.ToString(), Encoding.ASCII);
    }

    private static GitHubAsset? PickDownloadAsset(List<GitHubAsset>? assets)
    {
        if (assets == null || assets.Count == 0)
            return null;

        static bool HasUrl(GitHubAsset a) => !string.IsNullOrWhiteSpace(a.BrowserDownloadUrl);
        static string Name(GitHubAsset a) => a.Name ?? "";

        return assets.FirstOrDefault(a => HasUrl(a) && Name(a).EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                          && Name(a).Contains("TarkovTracker", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(a => HasUrl(a) && Name(a).EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(a => HasUrl(a)
                                          && Name(a).Equals("TarkovTracker.exe", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(a => HasUrl(a)
                                          && Name(a).EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                          && Name(a).Contains("TarkovTracker", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(a => HasUrl(a) && Name(a).EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
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

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
