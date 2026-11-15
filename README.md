# Darktide Mod Manager

A standalone Windows GUI for managing a Darktide Mod Loader installation.

## Run

Download the Windows x64 ZIP from the [latest release](https://github.com/jaSnom-dev/darktide-mod-manager/releases/latest), extract it, and run `DarktideModManager.exe`. It defaults to:

`C:\Program Files (x86)\Steam\steamapps\common\Warhammer 40,000 DARKTIDE`

The release ZIP contains the standalone executable and a quick-start/asset notice.

You can select a different game folder with **Browse...**. The app remembers the selected folder in `%LOCALAPPDATA%\DarktideModManager\settings.json`.

Use **Start Game** to launch Darktide through Steam.

The **Nexus Mods** button opens the Darktide mods page. The manager lists folders under `mods`, shows versions from each mod's `info.json` when available, preserves the existing load order, and appends newly found mods as disabled. Header comment lines are hidden from the list and preserved when saving. The `base` and `dmf` folders are omitted because they do not need load-order entries. Check or uncheck mods, use **Move up** / **Move down** to order dependencies, then choose **Apply changes**. The previous `mods\mod_load_order.txt` is kept as `mod_load_order.txt.bak`.

The Nexus Mods button uses the logo from [Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Nexus_Mods_Logo.svg). The Start Game button uses the Darktide app icon from [Steam's community assets](https://shared.akamai.steamstatic.com/community_assets/images/apps/1361210/c4e0990ff034cf6c48fb96649229e6362b866629.jpg). Both images are embedded in the executable.

Select an installed mod and choose **Update selected**:

- **Download latest from Nexus** searches Nexus for the installed mod folder name, lets you confirm the matching page, then checks and downloads the latest main file. The Nexus page is remembered per installed mod. If search is unavailable or finds no matches, you can enter the page URL or mod ID manually. The first download still requires your Nexus API key. The key is encrypted for your Windows account and stored locally.
- **Install update from ZIP...** installs a ZIP you have already downloaded. Archives must contain a recognized mod folder (`mod.txt` or a `.mod` file; `info.json` is optional); ambiguous archives are rejected.
- Use **Set Nexus page for selected mod...** or **Change Nexus API key...** in the same menu to update those saved values.

The name search uses Nexus's GraphQL v2 API, which Nexus documents as a work in progress; the manager offers manual URL/ID entry if the search is unavailable. If Nexus returns 403 for a download link, the manager can retry with the short-lived authorization in the website-generated `nxm://` link for that exact file. Premium users and files with direct downloads enabled should not need this step. The authorization is not saved. The old mod folder is retained under `mods\.DarktideModManager-backups`. If direct downloads remain unavailable, download the file through the Nexus page and use the ZIP option.

**Run patch helper** launches the Darktide Mod Loader's own `toggle_darktide_mods.bat`, where you can choose to patch or unpatch. The manager does not download mods or modify game bundles itself.

On startup, the app checks whether the loader patch file and loader binary are present. It also tracks the Darktide executable's version, size, and modified time; if those change after the last patch check, it warns that the patch may need updating. Patch status is shown in the bottom status line and refreshes after the patch helper closes.

## Build from source

Requires the .NET 10 SDK on Windows:

```powershell
dotnet publish DarktideModManager.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:DebugType=none -o publish-aligned
```

The result is `publish-aligned\DarktideModManager.exe`. The published app is self-contained and does not require the .NET runtime to be installed.
