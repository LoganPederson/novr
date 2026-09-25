using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using NOVR.Installer.Models;

namespace NOVR.Installer.Services;

public sealed class GitHubReleaseClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxChecksumFileBytes = 64 * 1024;

    private readonly HttpClient _httpClient;

    public GitHubReleaseClient()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(InstallerConstants.UserAgent);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    // Downloads the component's latest release zip and checks it against the SHA256SUMS.txt published with that
    // release. Throws, leaving nothing installed, if the checksum file or entry is missing or the hash differs.
    public async Task<(string ZipPath, ReleaseVersion Version)> DownloadVerifiedReleaseAsync(
        ModComponent component, string tempDir, IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report($"Checking latest {component.Name} release...");
        var release = await GetLatestReleaseAsync(component.Owner, component.Repo, cancellationToken);
        var version = (ReleaseVersion)release.TagName.TrimStart('v');

        var zipAsset = release.Assets.FirstOrDefault(asset => asset.Name == component.ZipAssetName)
                       ?? throw new InvalidOperationException($"{component.Name} {release.TagName} has no {component.ZipAssetName}.");
        var checksumAsset = release.Assets.FirstOrDefault(asset => asset.Name == InstallerConstants.ChecksumAssetName)
                            ?? throw new InvalidOperationException(
                                $"{component.Name} {release.TagName} has no {InstallerConstants.ChecksumAssetName}, so its download can't be verified.");

        var signatureAsset = release.Assets.FirstOrDefault(asset => asset.Name == InstallerConstants.ChecksumSignatureAssetName)
                             ?? throw new InvalidOperationException(
                                 $"{component.Name} {release.TagName} is not signed ({InstallerConstants.ChecksumSignatureAssetName} is missing), so it can't be trusted.");

        var expected = await ReadExpectedHashAsync(checksumAsset, signatureAsset, component, release.TagName, cancellationToken)
                       ?? throw new InvalidOperationException(
                           $"{InstallerConstants.ChecksumAssetName} for {component.Name} {release.TagName} has no entry for {component.ZipAssetName}.");

        progress.Report($"Downloading {component.Name} {version}...");
        var zipPath = await DownloadAsync(zipAsset.BrowserDownloadUrl, tempDir, component.ZipAssetName, cancellationToken);
        VerifySha256(zipPath, expected, $"{component.Name} {version}");
        return (zipPath, version);
    }

    // BepInEx is pinned: fixed URL, and a hash built into the installer.
    public async Task<string> DownloadPinnedBepInExAsync(string tempDir, IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report($"Downloading BepInEx {InstallerConstants.BepInExVersion}...");
        var zipPath = await DownloadAsync(InstallerConstants.BepInExDownloadUrl, tempDir, InstallerConstants.BepInExAssetName, cancellationToken);
        VerifySha256(zipPath, InstallerConstants.BepInExSha256, $"BepInEx {InstallerConstants.BepInExVersion}");
        return zipPath;
    }

    private static void VerifySha256(string path, string expectedHex, string what)
    {
        string actual;
        using (var stream = File.OpenRead(path))
            actual = Convert.ToHexString(SHA256.HashData(stream));

        if (!string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidOperationException(
                $"The download of {what} failed verification (SHA-256 mismatch) and was discarded. Nothing was installed.");
        }
    }

    // Only trusts the checksum list once its signature verifies against the built-in release key.
    private async Task<string?> ReadExpectedHashAsync(
        GitHubAsset checksumAsset, GitHubAsset signatureAsset, ModComponent component, string tag, CancellationToken cancellationToken)
    {
        if (checksumAsset.Size > MaxChecksumFileBytes || signatureAsset.Size > MaxChecksumFileBytes)
            throw new InvalidOperationException($"{InstallerConstants.ChecksumAssetName} or its signature is unexpectedly large.");

        var checksumBytes = await _httpClient.GetByteArrayAsync(checksumAsset.BrowserDownloadUrl, cancellationToken);
        var signature = await _httpClient.GetStringAsync(signatureAsset.BrowserDownloadUrl, cancellationToken);
        if (!ReleaseSignature.Verify(checksumBytes, signature))
        {
            throw new InvalidOperationException(
                $"The signature on {component.Name} {tag} is not valid for the release key {ReleaseSignature.TrustedKeyFingerprint}. " +
                "The release may have been tampered with; nothing was installed.");
        }

        return ParseChecksumFile(System.Text.Encoding.UTF8.GetString(checksumBytes), component.ZipAssetName);
    }

    // Format: one "<64 hex chars>  <file name>" per line (sha256sum style; a leading '*' on the name is allowed).
    internal static string? ParseChecksumFile(string text, string fileName)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf(' ');
            if (separator != 64) continue;

            var hash = line[..64];
            var name = line[64..].Trim().TrimStart('*');
            if (name == fileName && hash.All(Uri.IsHexDigit))
                return hash;
        }

        return null;
    }

    private async Task<GitHubRelease> GetLatestReleaseAsync(string owner, string repo, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        await using var stream = await _httpClient.GetStreamAsync(url, cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken);
        return release ?? throw new InvalidOperationException($"Could not read latest release for {owner}/{repo}.");
    }

    // The file name is always one of our own constants, never taken from the server.
    private async Task<string> DownloadAsync(string url, string tempDir, string fileName, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(tempDir);
        var destination = Path.Combine(tempDir, fileName);

        await using var remote = await _httpClient.GetStreamAsync(url, cancellationToken);
        await using var local = File.Create(destination);
        await remote.CopyToAsync(local, cancellationToken);
        return destination;
    }

    private sealed record GitHubRelease(
        [property: System.Text.Json.Serialization.JsonPropertyName("tag_name")]
        string TagName,
        GitHubAsset[] Assets);

    private sealed record GitHubAsset(
        string Name,
        long Size,
        [property: System.Text.Json.Serialization.JsonPropertyName("browser_download_url")]
        string BrowserDownloadUrl);
}
