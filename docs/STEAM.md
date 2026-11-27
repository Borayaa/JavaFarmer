# Play JavaFarmer from Steam (single exe)

`dist/JavaFarmer.exe` is one file that contains the whole game, BepInEx 5.4.23.6 and the JavaFarmer mod.
You add it to Steam as a "non-Steam game". This guide covers Linux (Proton) and Windows.

Build the exe first (see the README, "Single-exe build"):

```sh
scripts/build-exe.sh
```

## How the exe works

Unity games cannot run from inside an exe. The game needs its files in a normal folder. So the exe is a
small launcher with the game folder attached to it:

1. **First start:** the launcher unpacks the game (about 245 MB, about 1500 files) into
   `%LOCALAPPDATA%\JavaFarmer\<version id>\`. It checks every file against a SHA-256 list that is built into the exe.
   In our test this took 2.3 seconds. A slow disk takes longer.
2. **Every start:** the launcher checks the unpacked files (code and config files by SHA-256, other files by size).
   It puts back files that are missing or changed, and it deletes `.exe`/`.dll` files that do not belong there.
   Then it starts the game and waits until the game closes. In our test this check took 0.3 seconds.
3. The launcher reads no files next to itself and nothing from the current folder. It only reads its own exe and its
   unpack folder.

The launcher removes old version folders when you start a newer exe. Your settings in
`BepInEx/config/*.cfg` are copied to the new version. Your save files are never in the unpack folder (see below).

Disk space: the exe (about 90 MB) plus the unpacked game (about 245 MB).

## Linux (Steam with Proton)

1. Put `JavaFarmer.exe` in a folder where it can stay, for example `~/Games/JavaFarmer/JavaFarmer.exe`.
   If you move or rename it later, you must edit the Steam shortcut.
2. In Steam: **Games > Add a Non-Steam Game to My Library...**
3. Click **Browse...**. Set the file type filter to **All Files** (Steam shows only Linux programs by default).
   Select `JavaFarmer.exe` and click **Open**. Make sure it has a check mark, then click **Add Selected Programs**.
4. In the library, right-click **JavaFarmer.exe** > **Properties...** > **Compatibility**.
   Turn on **Force the use of a specific Steam Play compatibility tool** and select **Proton 11.0** or **Proton Experimental**.
5. Click **Play**.

You do not need launch options. The launcher sets the Wine DLL override that BepInEx needs
(`winhttp=native,builtin`, for `TheFarmerWasReplaced.exe` only, in the Proton prefix registry).

The first start takes longer: Proton creates a new prefix for the shortcut (this can take a minute), then the launcher
unpacks the game. Later starts are fast.

### Where the files are (Linux)

Steam gives each non-Steam shortcut its own Proton prefix in `steamapps/compatdata/<id>/`. The `<id>` is a large number
that Steam makes for the shortcut. After the first start, this command shows the folder:

```sh
find ~/.local/share/Steam/steamapps/compatdata -maxdepth 7 -type d -path '*AppData/Local/JavaFarmer'
```

(With the Flatpak version of Steam, use `~/.var/app/com.valvesoftware.Steam/.local/share/Steam` instead of
`~/.local/share/Steam`. With an extra Steam library, look in that library's `steamapps/compatdata`.)

With `PFX=~/.local/share/Steam/steamapps/compatdata/<id>/pfx/drive_c/users/steamuser`:

| What | Path |
|---|---|
| Unpacked game (cache) | `$PFX/AppData/Local/JavaFarmer/<version id>/` |
| Launcher log | `$PFX/AppData/Local/JavaFarmer/launcher.log` |
| BepInEx log | `$PFX/AppData/Local/JavaFarmer/<version id>/BepInEx/LogOutput.log` |
| Mod settings | `$PFX/AppData/Local/JavaFarmer/<version id>/BepInEx/config/javafarmer.plugin.cfg` |
| Save files | `$PFX/AppData/LocalLow/TheFarmerWasReplaced/TheFarmerWasReplaced/Saves/` |
| Game options, Unity log | `$PFX/AppData/LocalLow/TheFarmerWasReplaced/TheFarmerWasReplaced/` (`options.txt`, `Player.log`) |
| Steam emulator data | `$PFX/AppData/Roaming/GSE Saves/` |

The save path comes from the game itself: Unity `Application.persistentDataPath`, which is
`AppData/LocalLow/<company>/<product>`, and the game's company and product names are both `TheFarmerWasReplaced`.

### Move an existing save from another Wine prefix

A Wine prefix keeps the saves at:

```
<prefix>/drive_c/users/<user>/AppData/LocalLow/TheFarmerWasReplaced/TheFarmerWasReplaced/Saves/
```

`<user>` is your Linux user name for a normal Wine prefix (or `player` in our Docker test prefix), and `steamuser`
for a Proton prefix. For the Steam version of the game under Proton, the prefix is `steamapps/compatdata/2060160/pfx`.

1. Start JavaFarmer from Steam once, then quit the game. This makes the Proton prefix.
2. Back up the target folder if it has saves you want to keep.
3. Copy the saves:

```sh
SRC="/path/to/old/prefix/drive_c/users/<user>/AppData/LocalLow/TheFarmerWasReplaced/TheFarmerWasReplaced/Saves"
DST="$HOME/.local/share/Steam/steamapps/compatdata/<id>/pfx/drive_c/users/steamuser/AppData/LocalLow/TheFarmerWasReplaced/TheFarmerWasReplaced/Saves"
mkdir -p "$DST" && cp -a "$SRC/." "$DST/"
```

Each save is a `SaveN.json` file plus a `SaveN/` folder with the code files. Copy both.
Python code from a vanilla save does not compile as Java. Keep a backup of the old save.

### Name and artwork (optional)

- Name: **Properties > Shortcut**, change the name field (for example "The Farmer Was Replaced (Java)").
- Icon: the exe has the game's icon. Steam usually shows it. To change it, click the icon in **Properties > Shortcut**.
- Artwork: right-click the game in the library > **Manage** > **Set custom artwork** (and on the library page,
  right-click the header area to set a background or logo). You can use your own images, or images from a site
  such as SteamGridDB.

## Windows

1. Put `JavaFarmer.exe` in a folder where it can stay, for example `Documents\Games\JavaFarmer\`.
2. In Steam: **Games > Add a Non-Steam Game to My Library... > Browse...**, select `JavaFarmer.exe`,
   then **Add Selected Programs**.
3. Click **Play**. Do not set a compatibility tool or launch options.

The exe is not code-signed. On the first start Windows SmartScreen can show "Windows protected your PC".
Click **More info > Run anyway** only if you built the exe yourself or got it from a source you trust.
Some antivirus programs also distrust unsigned exes that contain a large attached archive.

Files on Windows:

| What | Path |
|---|---|
| Unpacked game, launcher log, BepInEx log | `%LOCALAPPDATA%\JavaFarmer\` (same layout as on Linux) |
| Save files | `%USERPROFILE%\AppData\LocalLow\TheFarmerWasReplaced\TheFarmerWasReplaced\Saves\` |

**On Windows the save folder is the same folder that a normal install of the game uses.** If you also play the
vanilla game on the same Windows account, JavaFarmer uses the same save slots. Back up that folder first, or use a new
save slot for Java.

## Troubleshooting

**The game starts, but the code is Python, not Java.** BepInEx or the mod did not load.

1. Open `BepInEx/LogOutput.log` in the unpack folder (see the tables above).
   - The file is missing: BepInEx did not start. On Linux, check that `launcher.log` has the line
     `Wine detected: DLL override winhttp=native,builtin set for TheFarmerWasReplaced.exe`.
     If your launch options set `WINEDLLOVERRIDES` with `winhttp` in it, remove that part: the environment variable
     has priority over the launcher's setting. As a last resort, set the launch option
     `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
   - The file has no line `JavaFarmer 0.1.0: N patches applied`: look for `[Error` or `[Warning` lines near
     `JavaFarmer`. If a required patch fails (for example after a game update), the mod removes all its patches and
     the game stays Python.
   - The line is there, but the game is still Python: open `BepInEx/config/javafarmer.plugin.cfg` and check
     `Enabled = true`.
2. Check that a new code window shows `void main() {`.

**An error box appears.** The box shows the reason. The same text is in `launcher.log`
(`AppData/Local/JavaFarmer/launcher.log`). "trailer magic not found" or "payload is corrupted" means that the exe
is incomplete: copy or build it again.

**My changes to the game files disappear.** This is intended. The launcher restores the files that it ships on
every start. Only `BepInEx/config/`, `BepInEx/cache/`, logs and other new non-executable files are kept.

**Clear the cache (unpack again).** Quit the game. Delete the `JavaFarmer` folder in `AppData/Local`
(on Linux: `$PFX/AppData/Local/JavaFarmer`; on Windows: `%LOCALAPPDATA%\JavaFarmer`). The next start unpacks the
game again. Your saves are in `AppData/LocalLow` and are not affected. Your mod settings are deleted with the
cache, so copy `BepInEx/config/javafarmer.plugin.cfg` first if you changed it.

**Check or repair without starting the game.** Run the exe with `--javafarmer-prepare-only`
(on Linux: put it in the shortcut's launch options, start once, then remove it). The result is in `launcher.log`.

**Update to a new build.** Replace `JavaFarmer.exe` with the new file at the same path. The next start unpacks the
new version and removes the old one. Settings and saves are kept.

**Remove everything.** Remove the shortcut from Steam (right-click > **Manage** > **Remove non-Steam game from
your library**). On Linux, also delete `steamapps/compatdata/<id>/` (this deletes the saves in that prefix: copy
them first). On Windows, delete `%LOCALAPPDATA%\JavaFarmer\`.
