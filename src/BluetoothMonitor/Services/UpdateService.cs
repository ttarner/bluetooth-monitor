using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace BluetoothMonitor.Services;

public sealed record UpdateInfo(
    string Version,
    string TagName,
    string ReleaseName,
    string ReleaseNotes,
    string DownloadUrl,
    long FileSizeBytes,
    DateTimeOffset PublishedAt);

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    UpdateInfo? Update,
    string? ErrorMessage);

public interface IUpdateService
{
    string CurrentVersion { get; }
    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    void ApplyUpdateAndRestart(string downloadedExePath);
}

public sealed class UpdateService : IUpdateService
{
    public const string DefaultRepository = "ttarner/bluetooth-monitor";
    private readonly string _repository;
    private readonly HttpClient _httpClient;

    public string CurrentVersion => GetInformationalVersion();

    public UpdateService(string repository = DefaultRepository, HttpClient? httpClient = null)
    {
        _repository = repository;
        _httpClient = httpClient ?? CreateDefaultHttpClient();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BluetoothMonitor-Updater", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        return client;
    }

    public static string GetInformationalVersion()
    {
        var informationalVersion = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Trim();

        return typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var apiUrl = $"https://api.github.com/repos/{_repository}/releases/latest";
        try
        {
            using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return new UpdateCheckResult(false, null, "No releases found for this repository.");

                return new UpdateCheckResult(false, null, $"GitHub API responded with status {response.StatusCode}.");
            }

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(jsonContent);
            var root = document.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            var releaseName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? tagName : tagName;
            var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
            var publishedAt = root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTimeOffset(out var pubDate)
                ? pubDate
                : DateTimeOffset.UtcNow;

            string? downloadUrl = null;
            long fileSize = 0;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var assetName = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                    if (string.Equals(assetName, "BluetoothMonitor.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var du) ? du.GetString() : null;
                        fileSize = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                        break;
                    }
                }

                // Fallback: search for any .exe asset if BluetoothMonitor.exe wasn't named exactly
                if (downloadUrl is null)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var assetName = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                        if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.TryGetProperty("browser_download_url", out var du) ? du.GetString() : null;
                            fileSize = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return new UpdateCheckResult(false, null, "No executable asset found in the latest release.");
            }

            var current = CurrentVersion;
            var isNewer = IsCandidateNewer(current, tagName);

            var updateInfo = new UpdateInfo(
                Version: CleanTagName(tagName),
                TagName: tagName,
                ReleaseName: releaseName,
                ReleaseNotes: body,
                DownloadUrl: downloadUrl,
                FileSizeBytes: fileSize,
                PublishedAt: publishedAt);

            return new UpdateCheckResult(isNewer, updateInfo, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(false, null, "Update check was canceled.");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, null, $"Failed to check for updates: {ex.Message}");
        }
    }

    public async Task<string> DownloadUpdateAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var updatesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BluetoothMonitor",
            "updates");

        Directory.CreateDirectory(updatesDir);

        var destinationPath = Path.Combine(updatesDir, "BluetoothMonitor.exe");
        var tempFilePath = Path.Combine(updatesDir, $"BluetoothMonitor_{Guid.NewGuid():N}.tmp");

        using var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? update.FileSizeBytes;

        await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                if (totalBytes > 0 && progress is not null)
                {
                    var percent = Math.Clamp((double)totalRead / totalBytes, 0d, 1d);
                    progress.Report(percent);
                }
            }
        }

        if (File.Exists(destinationPath))
            File.Delete(destinationPath);

        File.Move(tempFilePath, destinationPath);
        return destinationPath;
    }

    public void ApplyUpdateAndRestart(string downloadedExePath)
    {
        if (!File.Exists(downloadedExePath))
            throw new FileNotFoundException("Downloaded update binary not found.", downloadedExePath);

        var currentExePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExePath))
            currentExePath = Path.Combine(AppContext.BaseDirectory, "BluetoothMonitor.exe");

        var currentProcessId = Environment.ProcessId;

        // PowerShell script waits for current process to exit, replaces the executable, and launches the new one.
        var script = $"Wait-Process -Id {currentProcessId} -Timeout 15 -ErrorAction SilentlyContinue; " +
                     $"Start-Sleep -Milliseconds 300; " +
                     $"Move-Item -LiteralPath '{downloadedExePath}' -Destination '{currentExePath}' -Force; " +
                     $"Start-Process -FilePath '{currentExePath}'";

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-WindowStyle Hidden -NoProfile -NonInteractive -Command \"{script}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        Process.Start(startInfo);
        System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
    }

    internal static string CleanTagName(string tagName) =>
        tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tagName[1..].Trim() : tagName.Trim();

    internal static bool IsCandidateNewer(string currentVersion, string candidateTag)
    {
        if (string.IsNullOrWhiteSpace(candidateTag))
            return false;

        var cleanCandidate = CleanTagName(candidateTag);
        var cleanCurrent = CleanTagName(currentVersion);

        // Exact match
        if (string.Equals(cleanCurrent, cleanCandidate, StringComparison.OrdinalIgnoreCase))
            return false;

        // If current is "1.0.0+<commitSha>"
        var currentPlusIndex = cleanCurrent.IndexOf('+');
        string currentCommitSha = "";
        if (currentPlusIndex >= 0)
        {
            currentCommitSha = cleanCurrent[(currentPlusIndex + 1)..].Trim();
            cleanCurrent = cleanCurrent[..currentPlusIndex].Trim();
        }

        // Check if candidate tag has format "yyyy.MM.dd-<shortSha>"
        var dateShaMatch = Regex.Match(cleanCandidate, @"^(\d{4})\.(\d{2})\.(\d{2})-([a-fA-F0-9]+)$");
        if (dateShaMatch.Success)
        {
            var candidateDate = new DateTime(
                int.Parse(dateShaMatch.Groups[1].Value),
                int.Parse(dateShaMatch.Groups[2].Value),
                int.Parse(dateShaMatch.Groups[3].Value),
                0, 0, 0, DateTimeKind.Utc);
            var candidateSha = dateShaMatch.Groups[4].Value;

            // If current commit SHA matches candidate SHA, it's the exact same build
            if (!string.IsNullOrEmpty(currentCommitSha) && currentCommitSha.StartsWith(candidateSha, StringComparison.OrdinalIgnoreCase))
                return false;

            // If current version is also a date-based tag "yyyy.MM.dd-<shortSha>"
            var currentDateMatch = Regex.Match(cleanCurrent, @"^(\d{4})\.(\d{2})\.(\d{2})-([a-fA-F0-9]+)$");
            if (currentDateMatch.Success)
            {
                var currentDate = new DateTime(
                    int.Parse(currentDateMatch.Groups[1].Value),
                    int.Parse(currentDateMatch.Groups[2].Value),
                    int.Parse(currentDateMatch.Groups[3].Value),
                    0, 0, 0, DateTimeKind.Utc);
                var currentTagSha = currentDateMatch.Groups[4].Value;

                if (candidateDate > currentDate) return true;
                if (candidateDate < currentDate) return false;
                return !string.Equals(currentTagSha, candidateSha, StringComparison.OrdinalIgnoreCase);
            }

            // If current is a semver or dev version, and candidate is date-sha
            return true;
        }

        // Semantic Version comparison fallback
        if (Version.TryParse(cleanCandidate, out var candidateVer) && Version.TryParse(cleanCurrent, out var currentVer))
        {
            return candidateVer > currentVer;
        }

        // Default: if tag differs from current version, consider update available
        return !string.Equals(cleanCurrent, cleanCandidate, StringComparison.OrdinalIgnoreCase);
    }
}
