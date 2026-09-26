namespace NOVR.Installer.Models;

// A mod the installer manages: where its releases come from, and the only folders (relative to BepInEx/) it may
// write. Installing replaces exactly those folders and nothing else, whatever else the release zip contains.
public sealed record ModComponent(
    string Name,
    string Owner,
    string Repo,
    string ZipAssetName,
    string[] Folders,
    bool Required)
{
    // The first folder holds the installed version file.
    public string VersionFolder => Folders[0];

    public static readonly ModComponent Novr = new(
        "NOVR",
        InstallerConstants.GitHubOwner,
        InstallerConstants.GitHubRepo,
        "NOVR.zip",
        new[] { "plugins/NOVR", "patchers/NOVR" },
        Required: true);

    public static readonly ModComponent MessageLog = new(
        "Message Log",
        InstallerConstants.GitHubOwner,
        "no-message-log",
        "MessageLog.zip",
        new[] { "plugins/MessageLog" },
        Required: false);

    public static readonly ModComponent Comms = new(
        "Comms",
        InstallerConstants.GitHubOwner,
        "no-comms",
        "Comms.zip",
        new[] { "plugins/Comms" },
        Required: false);

    public static readonly IReadOnlyList<ModComponent> Catalog = new[] { Novr, MessageLog, Comms };
}
