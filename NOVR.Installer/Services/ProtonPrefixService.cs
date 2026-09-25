using System.Diagnostics;
using System.Runtime.InteropServices;
using NOVR.Installer.Models;

namespace NOVR.Installer.Services;

public sealed class ProtonPrefixService
{
    public async Task<string> TryConfigureWinHttpOverrideAsync(GameInstallInfo game, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Proton/Wine override is only needed on Linux.";
        }

        progress.Report("Looking for Proton prefix...");
        var prefix = FindPrefix(game.GameDir);
        if (prefix is null)
        {
            return "Could not find the Proton prefix automatically. Set winhttp to native,builtin in winecfg for this game.";
        }

        var wine = FindExecutable("wine") ?? FindExecutable("wine64");
        if (wine is null)
        {
            return $"Found prefix at {prefix}, but wine was not available. Set winhttp to native,builtin manually.";
        }

        progress.Report("Configuring winhttp Wine override...");
        var result = await RunProcessAsync(
            wine,
            "reg add \"HKCU\\Software\\Wine\\DllOverrides\" /v winhttp /d native,builtin /f",
            new Dictionary<string, string?> { ["WINEPREFIX"] = prefix },
            cancellationToken);

        return result == 0
            ? "Configured winhttp override for Proton/Wine."
            : $"Could not configure winhttp automatically. Set winhttp to native,builtin manually for prefix: {prefix}";
    }

    // Nuclear Option's Steam app id; Proton keeps each game's prefix in steamapps/compatdata/<appid>/pfx.
    private const string NuclearOptionAppId = "2168680";

    // Only ever returns Nuclear Option's own prefix. Picking another game's prefix would set winhttp to
    // native,builtin there, making that game load any winhttp.dll in its folder.
    private static string? FindPrefix(string gameDir)
    {
        var candidates = new List<string>();

        // The prefix lives in the same Steam library as the game: <library>/steamapps/common/Nuclear Option.
        var steamApps = Directory.GetParent(Path.GetFullPath(gameDir))?.Parent;
        if (steamApps is not null)
        {
            candidates.Add(Path.Combine(steamApps.FullName, "compatdata", NuclearOptionAppId, "pfx"));
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in new[]
                 {
                     Path.Combine(home, ".steam", "steam"),
                     Path.Combine(home, ".steam", "debian-installation"),
                     Path.Combine(home, ".local", "share", "Steam")
                 })
        {
            candidates.Add(Path.Combine(root, "steamapps", "compatdata", NuclearOptionAppId, "pfx"));
        }

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static string? FindExecutable(string name)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return paths.Select(path => Path.Combine(path, name)).FirstOrDefault(File.Exists);
    }

    private static async Task<int> RunProcessAsync(
        string executable,
        string arguments,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo.FileName = executable;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        foreach (var (key, value) in environment)
        {
            process.StartInfo.Environment[key] = value;
        }

        process.Start();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
