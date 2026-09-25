using System.IO.Compression;
using NOVR.Installer.Models;

namespace NOVR.Installer.Services;

public sealed class FileSystemInstaller
{
    // The BepInEx zip is verified against the pinned hash before this runs, so extracting it over the game is safe.
    public async Task InstallBepInExAsync(GameInstallInfo game, string bepInExZip, IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report("Installing BepInEx...");
        await Task.Run(() =>
        {
            ZipFile.ExtractToDirectory(bepInExZip, game.GameDir, overwriteFiles: true);
        }, cancellationToken);
    }

    // Replaces exactly the component's own folders under BepInEx/ with the ones from its release zip. Anything
    // else in the zip is ignored, so a release can never overwrite BepInEx itself or another mod.
    public async Task InstallComponentAsync(GameInstallInfo game, ModComponent component, string zip, string version, IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report($"Installing {component.Name} {version}...");
        var tempExtract = Path.Combine(Path.GetTempPath(), "novr-installer-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Task.Run(() =>
            {
                ZipFile.ExtractToDirectory(zip, tempExtract);

                foreach (var folder in component.Folders)
                {
                    if (!Directory.Exists(Path.Combine(tempExtract, folder)))
                        throw new InvalidOperationException($"The {component.Name} zip is missing its {folder} folder.");
                }

                foreach (var folder in component.Folders)
                {
                    var destination = game.BepInExPath(folder);
                    TryDeleteDirectory(destination);
                    CopyDirectory(Path.Combine(tempExtract, folder), destination);
                }

                File.WriteAllText(Path.Combine(game.BepInExPath(component.VersionFolder), InstallerConstants.VersionFileName), version);
            }, cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(tempExtract);
        }
    }

    public async Task UninstallComponentAsync(GameInstallInfo game, ModComponent component, IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report($"Removing {component.Name}...");
        await Task.Run(() =>
        {
            foreach (var folder in component.Folders)
                TryDeleteDirectory(game.BepInExPath(folder));
        }, cancellationToken);
    }

    public async Task UninstallBepInExAsync(GameInstallInfo game, IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report("Removing BepInEx...");
        await Task.Run(() =>
        {
            TryDeleteDirectory(game.BepInExDir);
            TryDeleteFile(Path.Combine(game.GameDir, "winhttp.dll"));
            TryDeleteFile(Path.Combine(game.GameDir, "doorstop_config.ini"));
        }, cancellationToken);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destinationFile = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive: true);
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
