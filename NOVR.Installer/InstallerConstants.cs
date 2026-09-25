namespace NOVR.Installer;

public static class InstallerConstants
{
    public const string AppName = "Nuclear Option VR";
    // Releases come from this fork; see ModComponent.Catalog for each component's repository.
    public const string GitHubOwner = "LoganPederson";
    public const string GitHubRepo = "novr";
    public const string UserAgent = "NOVR.Installer";

    public const string PluginsFolderName = "plugins";
    public const string PatchersFolderName = "patchers";
    public const string ModFolderName = "NOVR";

    // Every release lists "<sha256>  <file name>" for its assets here; downloads that don't match are rejected.
    public const string ChecksumAssetName = "SHA256SUMS.txt";

    // BepInEx is pinned to one reviewed release and verified against a hash built into the installer, so a
    // new or tampered upstream release can't be installed without an installer update.
    public const string BepInExVersion = "5.4.23.5";
    public const string BepInExAssetName = "BepInEx_win_x64_5.4.23.5.zip";
    public const string BepInExDownloadUrl = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip";
    public const string BepInExSha256 = "82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4";

    public const string VersionFileName = "version.txt";
}
