# [Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/) VR

NOVR is a reworked version of [UUVR](https://github.com/Raicuparta/uuvr) designed and optimized for Nuclear Option.

## User Installation

### Recommended: GUI installer

1. Close Nuclear Option before installing or updating the mod.
2. Download the latest installer from the [NOVR releases page](https://github.com/LoganPederson/novr/releases/latest):
    - **Windows:** `NOVR.Installer-Win.exe`
    - **Linux/Proton:** `NOVR.Installer-Linux`
3. Run the installer directly.
    - On Linux, you may need to make it executable first: `chmod +x NOVR.Installer-Linux`.
4. If Nuclear Option is not found automatically, choose the game folder manually.
    - The selected folder must contain `NuclearOption_Data/Managed`.
5. Click **Install**.
6. Launch Nuclear Option from Steam.

The installer can also update, repair, or uninstall NOVR after it is installed.

### What the installer does

The installer:

- Finds your Nuclear Option install, in any Steam library.
- Installs BepInEx 5.4.23.5 if it is missing, verified against a SHA-256 built into the installer.
- Downloads the latest NOVR release zip, and optionally [Message Log](https://github.com/LoganPederson/no-message-log) (chat and kill history, weapons in the kill feed).
- Checks each download against the `SHA256SUMS.txt` published with its release, and installs nothing if a file doesn't match.
- Installs NOVR into `BepInEx/plugins/NOVR` and `BepInEx/patchers/NOVR`, and Message Log into `BepInEx/plugins/MessageLog`. Each mod only ever replaces its own folders.
- Writes each installed version to a `version.txt` in its plugin folder.
- On Linux/Proton, attempts to configure the `winhttp` Wine override needed by BepInEx.

Use BepInEx 5.x only. Do not use BepInEx 6.x unless the project explicitly says it is supported.

### Manual zip install

Use this only if the installer does not work for your setup.

1. Close Nuclear Option.
2. Install [BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) (the version the installer uses) into the Nuclear Option game folder.
    - After installing BepInEx, this folder should exist: `Nuclear Option/BepInEx/core`.
3. Download `NOVR.zip` from the [latest NOVR release](https://github.com/LoganPederson/novr/releases/latest).
4. Extract the contents of `NOVR.zip` into `Nuclear Option/BepInEx`.
5. Confirm these files exist:

    ```text
    Nuclear Option/BepInEx/plugins/NOVR/NOVR.dll
    Nuclear Option/BepInEx/patchers/NOVR/NOVR.Patcher.dll
    ```

6. Launch Nuclear Option from Steam.

`NOVR.zip` contains `plugins` and `patchers` folders. Extract it into the existing `BepInEx` folder, not directly into the game root.

### First launch behavior

On game startup, the NOVR BepInEx patcher copies required XR support files into `NuclearOption_Data`. These files may be overwritten every time the game starts.

If the game is already running while installing or rebuilding, Windows may prevent those files from being replaced. Close Nuclear Option before installing, updating, or building the mod.

### Recentering the view

Press **F9** to recenter your view, in the cockpit or in menus. The recenter happens after a short countdown so you have time to face forward. In flight you can also use **RECENTER VIEW** in the pause menu. The key can be changed with `Recenter Shortcut` in `BepInEx/config/deltawing.novr.cfg`; HOTAS users can map a stick button to it with their joystick software.

### Hand tracking for menus

If your headset tracks hands and your OpenXR runtime supports `XR_EXT_hand_tracking` (for example Virtual Desktop's VDXR or Meta Quest Link), you can use menus without controllers. With Virtual Desktop, turn on **Forward tracking data** in its Streaming tab first. Then raise a hand in front of you, point, and pinch your thumb and index finger to click. Hands resting low, such as on a stick and throttle, are ignored, so they can't click by accident. Adjust `Hand Raise Height` in the config if your HOTAS sits unusually high or pointing is hard to start, or turn `Hand Tracking` off entirely.

### Map and target screen in flight

In the cockpit, point at the small map on the HUD and pinch (or click it with a controller) to open the full map; the game's map key works too. On the full map, pinch an icon to select it, and pinch and drag to pan. A bar under the map lets you place it: press and drag **MOVE** to move it around you (with a hand, pull in or push out to bring it nearer or farther), **-** and **+** resize it, **RESET** puts it back where the game has it, and **CLOSE** closes the map. **TGT SCREEN** shows a floating copy of the cockpit's target screen, which you can move and size the same way. Placements are saved in the `[Panels]` section of the config, which also has optional keys (unset by default) to toggle the map and target screen, bring panels to where you're looking, and resize them.

### Linux/Proton notes

The installer tries to set the required `winhttp` override automatically. If BepInEx does not load under Proton, configure the game's Wine prefix manually so `winhttp` uses `native,builtin`.

See the [BepInEx running under Proton guide](https://docs.bepinex.dev/articles/advanced/proton_wine.html) for the manual setup.

## Building

These steps are for developers building NOVR from source.

1. Install a .NET/MSBuild toolchain that can build SDK-style .NET Framework 4.8 projects.
    - **Windows:** Visual Studio 2022 or Build Tools for Visual Studio 2022 with the **.NET Framework 4.8 targeting pack** installed.
    - **Linux:** the .NET SDK plus Mono/MSBuild and .NET Framework reference assemblies. Distro package names vary, but you usually want packages such as `dotnet-sdk`, `mono`, `msbuild`/`mono-msbuild`, and `mono-reference-assemblies`/`.NET Framework 4.8 reference assemblies`.
2. Have Nuclear Option installed. The build looks for it in:
    - **Windows:** the main library of your Steam install (found through the registry, wherever Steam is installed), then `C:\Program Files (x86)\Steam`, `C:\Program Files\Steam` and `D:\SteamLibrary`.
    - **Linux:** `~/.steam/steam/steamapps/common/Nuclear Option` OR `~/.steam/debian-installation/steamapps/common/Nuclear Option` OR `~/.local/share/Steam/steamapps/common/Nuclear Option`

    If the game is in a different Steam library, set the `NUCLEAR_OPTION_GAME_DIR` environment variable or pass `/p:NuclearOptionGameDir="path\to\Nuclear Option"` when building.
3. Ensure BepInEx 5.x is installed inside the Nuclear Option directory.
4. Build the `Release` configuration from your IDE of choice. JetBrains Rider is tested; Visual Studio should work too. To build from the command line, run this from the project root:

    ```bash
    dotnet build NuclearOptionVirtualRealityMod.sln -c Release
    ```

    The build output is written under `build-output`, as well as copied directly to the BepInEx directory.

## Installer development

The GUI installer is built with Avalonia. Building it in `Release` automatically publishes raw installer binaries for Linux and Windows into `dist/`:

```bash
dotnet build NOVR.Installer/NOVR.Installer.csproj -c Release
```

Outputs:

- `dist/NOVR.Installer-Linux`
- `dist/NOVR.Installer-Win.exe`

Building the full solution in `Release` also creates `dist/NOVR.zip`, ready to upload as the mod release asset.

## Versioning, CI and releases

The mod version lives in one place: `NovrVersion` in `NOVR.Build/NOVR.Build.props`. The plugin's `BepInPlugin` version is generated from it at build time.

GitHub Actions builds every push and pull request (`.github/workflows/ci.yml`) and uploads `dist/` as a build artifact. Pushing a tag such as `v0.4.5` (`.github/workflows/release.yml`) builds with that version and creates a **draft** GitHub release with `NOVR.zip` and both installers attached, ready to review and publish.

The NOVR plugin compiles against the game's assemblies, which can't be committed. CI uses reference-only copies instead: every method body and private member is stripped, and the resulting `NOVR.dll` is byte-identical to one built against the real game. To set this up, or refresh it after a game update:

1. Run `tools/Export-GameReferences.ps1` on a machine with the game and BepInEx installed. It writes `game-refs/` (git-ignored).
2. Push the contents of `game-refs/` to a **private** repository.
3. Add a read-only deploy key to that repository, and in this repository's GitHub settings add an Actions variable `GAME_REFS_REPO` (`owner/name` of that repository) and a secret `GAME_REFS_SSH_KEY` holding the deploy key's private half.

Without these, CI still builds the patcher, XR libraries and installers, and notes that the plugin was skipped. Releases require them.

## License

    Nuclear Option VR Mod
    Copyright (C) 2026 InfernoSuperNova (DeltaWing)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.

# Original README below

> # Universal Unity VR
>
> [![Raicuparta's VR mods](https://raicuparta.com/img/badge.svg)](https://raicuparta.com)
>
> Use [Rai Pal](https://pal.raicuparta.com) to install this mod.
>
> ## License
>
>     Rai Pal
>     Copyright (C) 2024  Raicuparta
>
>     This program is free software: you can redistribute it and/or modify
>     it under the terms of the GNU General Public License as published by
>     the Free Software Foundation, either version 3 of the License, or
>     (at your option) any later version.
>
>     This program is distributed in the hope that it will be useful,
>     but WITHOUT ANY WARRANTY; without even the implied warranty of
>     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
>     GNU General Public License for more details.
>
>     You should have received a copy of the GNU General Public License
>     along with this program.  If not, see <https://www.gnu.org/licenses/>.

## Verifying a release

Every release includes `SHA256SUMS.txt`, signed with the release key (`SHA256SUMS.txt.asc`). The installer checks both automatically. To check by hand:

```
gpg --import RELEASE-SIGNING-KEY.asc
gpg --verify SHA256SUMS.txt.asc SHA256SUMS.txt
sha256sum -c SHA256SUMS.txt
```

The release key's fingerprint is `8DC8 AFFC F116 E35D 08CC  F300 5EBB 096D 8D58 8294` (Logan Pederson, Nuclear Option mods release signing).
