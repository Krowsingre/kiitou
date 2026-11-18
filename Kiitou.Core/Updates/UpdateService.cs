using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kiitou.Updates;

/// <summary>Something about an update went wrong in a way the user should be told.</summary>
public sealed class UpdateException(string message) : Exception(message);

/// <summary>The result of asking whether a newer version exists.</summary>
public sealed record UpdateInfo(Version Current, Version? Available, string? Notes, string? Url, string? Signature)
{
    public bool IsAvailable => Available is not null;
}

/// <summary>
/// Kiitou updating itself. It reads a small description of the newest release from GitHub, and
/// if that is newer, downloads the setup program, confirms it was signed with Kiitou's own
/// update key, and runs it quietly. An update that is not correctly signed is never run.
/// </summary>
public sealed class UpdateService
{
    public const string FeedUrl = "https://github.com/Krowsingre/kiitou/releases/latest/download/latest.json";
    /// <summary>Downloads are only accepted from this project's own releases.</summary>
    public const string DownloadPrefix = "https://github.com/Krowsingre/kiitou/releases/download/";

    /// <summary>The public half of the update-signing key. The private half is a GitHub secret.</summary>
    public const string PublicKeyPem =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEvU76vLE0NkQQ1b5PiCJRP82caw7F\n" +
        "jc2bd3gsedkGzg2aHhAEM8wKvl8CeyxX4Hd+SWOm2YSvyuAKbhX+P3huBg==\n" +
        "-----END PUBLIC KEY-----";

    private const long MaximumDownloadBytes = 600L * 1024 * 1024;
    private readonly Version _current;
    private readonly HttpMessageHandler? _handler;
    private readonly string _publicKeyPem;
    private readonly string _feedUrl;
    private readonly string _downloadFolder;

    public UpdateService(
        Version current, HttpMessageHandler? handler = null, string? publicKeyPem = null,
        string? feedUrl = null, string? downloadFolder = null)
    {
        _current = Normalize(current);
        _handler = handler;
        _publicKeyPem = publicKeyPem ?? PublicKeyPem;
        _feedUrl = feedUrl ?? FeedUrl;
        _downloadFolder = downloadFolder ?? DefaultDownloadFolder;
    }

    public Version Current => _current;

    /// <summary>The folder downloaded setup programs and the update window's copy of Kiitou are kept in.</summary>
    public string DownloadFolder => _downloadFolder;

    /// <summary>The default download folder, used by the uninstaller as well.</summary>
    public static string DefaultDownloadFolder => Path.Combine(Path.GetTempPath(), "Kiitou-update");

    /// <summary>
    /// Deletes everything left from earlier updates. Files still in use (for example by an update
    /// window that is just closing) are skipped and removed next time. Returns how many were removed.
    /// </summary>
    public int CleanUpDownloads()
    {
        if (!Directory.Exists(_downloadFolder)) return 0;
        int removed = 0;
        foreach (string file in Directory.GetFiles(_downloadFolder))
        {
            try
            {
                File.Delete(file);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        try
        {
            if (!Directory.EnumerateFileSystemEntries(_downloadFolder).Any()) Directory.Delete(_downloadFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return removed;
    }

    /// <summary>
    /// Confirms that a setup program already on disk was signed with Kiitou's update key.
    /// </summary>
    /// <exception cref="UpdateException">The signature is missing or does not match.</exception>
    public void VerifyFile(string path, string? signatureBase64)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64 ?? "");
        }
        catch (FormatException)
        {
            signature = [];
        }
        if (signature.Length == 0 || !File.Exists(path) || !IsSignedByKiitou(path, signature))
            throw new UpdateException("The update was not signed with Kiitou's update key, so it was not installed.");
    }

    /// <exception cref="UpdateException">GitHub could not be reached or its answer could not be read.</exception>
    public async Task<UpdateInfo> CheckAsync(CancellationToken cancellationToken)
    {
        string json;
        try
        {
            using HttpClient client = CreateClient(TimeSpan.FromSeconds(15));
            using HttpResponseMessage response = await client.GetAsync(_feedUrl, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new UpdateInfo(_current, null, null, null, null);
            if (!response.IsSuccessStatusCode)
                throw new UpdateException($"Kiitou couldn't check for updates (GitHub answered {(int)response.StatusCode}).");
            json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            throw new UpdateException("Kiitou couldn't reach GitHub to check for updates.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string? versionText = Text(root, "version");
            if (!Version.TryParse(versionText, out Version? remote))
                throw new UpdateException("The update information from GitHub could not be read.");
            remote = Normalize(remote);
            return remote > _current
                ? new UpdateInfo(_current, remote, Text(root, "notes"), Text(root, "url"), Text(root, "signature"))
                : new UpdateInfo(_current, null, null, null, null);
        }
        catch (JsonException)
        {
            throw new UpdateException("The update information from GitHub could not be read.");
        }
    }

    /// <summary>
    /// Downloads the setup program for an update and confirms its signature.
    /// Returns the path of the verified file.
    /// </summary>
    /// <exception cref="UpdateException">The download failed or the file is not correctly signed.</exception>
    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!update.IsAvailable || string.IsNullOrEmpty(update.Url))
            throw new UpdateException("There is no update to install.");
        if (!update.Url.StartsWith(DownloadPrefix, StringComparison.Ordinal) ||
            !update.Url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || update.Url.Contains(".."))
            throw new UpdateException("The update does not come from Kiitou's own releases, so it was not downloaded.");

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(update.Signature ?? "");
        }
        catch (FormatException)
        {
            signature = [];
        }
        if (signature.Length == 0)
            throw new UpdateException("The update is not signed, so it was not downloaded.");

        Directory.CreateDirectory(_downloadFolder);
        string path = Path.Combine(_downloadFolder, $"Kiitou-Setup-{update.Available}.exe");
        try
        {
            using HttpClient client = CreateClient(TimeSpan.FromMinutes(30));
            using HttpResponseMessage response = await client
                .GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new UpdateException($"The update could not be downloaded (GitHub answered {(int)response.StatusCode}).");
            long? total = response.Content.Headers.ContentLength;
            if (total > MaximumDownloadBytes) throw new UpdateException("The update is unexpectedly large and was not downloaded.");

            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    received += read;
                    if (received > MaximumDownloadBytes) throw new UpdateException("The update is unexpectedly large and was not downloaded.");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    if (total > 0) progress?.Report((double)received / total.Value);
                }
            }

            if (!IsSignedByKiitou(path, signature))
                throw new UpdateException("The update was not signed with Kiitou's update key, so it was not installed.");
            return path;
        }
        catch (Exception ex)
        {
            TryDelete(path);
            if (ex is UpdateException) throw;
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
            if (ex is HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException)
                throw new UpdateException("The update could not be downloaded.");
            throw;
        }
    }

    /// <summary>
    /// Runs the verified setup program quietly and waits for it. Kiitou is not started by the
    /// setup program; the caller starts it. Returns the setup program's exit code.
    /// </summary>
    public static async Task<int> RunInstallerAsync(string verifiedSetupPath, string logPath, TimeSpan timeout)
    {
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = verifiedSetupPath,
                Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LAUNCH=0 /LOG=\"{logPath}\"",
                UseShellExecute = false
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            throw new UpdateException("The update could not be started: " + ex.Message);
        }
        if (process is null) throw new UpdateException("The update could not be started.");
        using (process)
        {
            using var limit = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new UpdateException("The update took too long and was stopped waiting for.");
            }
            return process.ExitCode;
        }
    }

    /// <summary>True when an installer log records an internal or script error.</summary>
    public static bool LogHasErrors(string logPath)
    {
        try
        {
            string text = File.ReadAllText(logPath);
            return text.Contains("Internal error", StringComparison.Ordinal) ||
                   text.Contains("Exception message", StringComparison.Ordinal) ||
                   text.Contains("Runtime error", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool IsSignedByKiitou(string path, byte[] signature)
    {
        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportFromPem(_publicKeyPem);
            using FileStream file = File.OpenRead(path);
            return key.VerifyData(file, signature, HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private HttpClient CreateClient(TimeSpan timeout)
    {
        HttpClient client = _handler is null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false);
        client.Timeout = timeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Kiitou-Updater");
        return client;
    }

    /// <summary>Compares on major.minor.build only, so 0.9.0 and 0.9.0.0 are the same version.</summary>
    private static Version Normalize(Version version) =>
        new(Math.Max(0, version.Major), Math.Max(0, version.Minor), Math.Max(0, version.Build));

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
