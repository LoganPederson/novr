using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using NOVR.Installer.Models;

namespace NOVR.Installer.Services;

public sealed partial class GameLocator
{
    public IReadOnlyList<string> GetCandidatePaths()
    {
        var candidates = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var envPath = Environment.GetEnvironmentVariable("NUCLEAR_OPTION_GAME_DIR");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            candidates.Add(envPath);
        }

        candidates.AddRange(GetSteamLibraryCandidates(home));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            candidates.Add(@"C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option");
            candidates.Add(@"C:\Program Files\Steam\steamapps\common\Nuclear Option");
            candidates.Add(@"D:\SteamLibrary\steamapps\common\Nuclear Option");
        }
        else
        {
            candidates.Add(Path.Combine(home, "Locations", "NuclearOption", "Install"));
            candidates.Add(Path.Combine(home, ".steam", "steam", "steamapps", "common", "Nuclear Option"));
            candidates.Add(Path.Combine(home, ".steam", "debian-installation", "steamapps", "common", "Nuclear Option"));
            candidates.Add(Path.Combine(home, ".local", "share", "Steam", "steamapps", "common", "Nuclear Option"));
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public GameInstallInfo? FindGame()
    {
        return GetCandidatePaths()
            .Select(Inspect)
            .FirstOrDefault(info => info.IsValid);
    }

    public GameInstallInfo Inspect(string gameDir)
    {
        var normalized = ExpandHome(gameDir.Trim().Trim('"'));
        var managedDir = Path.Combine(normalized, "NuclearOption_Data", "Managed");
        var isValid = Directory.Exists(managedDir);

        if (!isValid)
        {
            return new GameInstallInfo(normalized, false, false, InstallState.Unknown, "Nuclear Option was not found at this path.");
        }

        var bepInExCore = Path.Combine(normalized, "BepInEx", "core");
        var hasBepInEx = Directory.Exists(bepInExCore);
        var pluginDir = Path.Combine(normalized, "BepInEx", "plugins", InstallerConstants.ModFolderName);
        var patcherDir = Path.Combine(normalized, "BepInEx", "patchers", InstallerConstants.ModFolderName);
        var hasPlugin = Directory.Exists(pluginDir) && Directory.EnumerateFileSystemEntries(pluginDir).Any();
        var hasPatcher = Directory.Exists(patcherDir) && Directory.EnumerateFileSystemEntries(patcherDir).Any();

        var state = (hasPlugin, hasPatcher) switch
        {
            (true, true) => InstallState.FullyInstalled,
            (false, false) => InstallState.NotInstalled,
            _ => InstallState.PartiallyInstalled
        };

        return new GameInstallInfo(normalized, true, hasBepInEx, state, null);
    }

    // Every Steam library listed in libraryfolders.vdf, so installs outside the default library are found.
    private static IEnumerable<string> GetSteamLibraryCandidates(string home)
    {
        var steamRoots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steamPath &&
                !string.IsNullOrWhiteSpace(steamPath))
            {
                steamRoots.Add(Path.GetFullPath(steamPath));
            }
        }
        else
        {
            steamRoots.Add(Path.Combine(home, ".steam", "steam"));
            steamRoots.Add(Path.Combine(home, ".steam", "debian-installation"));
            steamRoots.Add(Path.Combine(home, ".local", "share", "Steam"));
            steamRoots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
        }

        var libraries = new List<string>();
        foreach (var steamRoot in steamRoots)
        {
            libraries.Add(steamRoot);
            var libraryFolders = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFolders))
            {
                continue;
            }

            try
            {
                libraries.AddRange(ParseLibraryFolders(File.ReadAllText(libraryFolders)));
            }
            catch (IOException)
            {
                // Unreadable library list; the default library above is still tried.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return libraries.Select(library => Path.Combine(library, "steamapps", "common", "Nuclear Option"));
    }

    internal static IEnumerable<string> ParseLibraryFolders(string vdf)
    {
        foreach (Match match in LibraryPathRegex().Matches(vdf))
        {
            yield return match.Groups[1].Value.Replace(@"\\", @"\");
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathRegex();

    private static string ExpandHome(string path)
    {
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }

        return path;
    }
}
