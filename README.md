# JavaFarmer

A mod for [The Farmer Was Replaced](https://store.steampowered.com/app/2060160/The_Farmer_Was_Replaced/)
that lets you program the drone in **Java, C, C++, C#, Rust, Zig, JavaScript or R** instead of the game's Python
(or keep Python). **Every code file chooses its own language by its file extension**: name a code window `main.java`,
`utils.rs`, `bot.py` or `farm.c`, and files of different languages can call each other. The Mods menu sets the
default language for new files and for the docs (default: Java).

The mod translates your code to the game's own Python dialect in the background. The game then runs it
with its own interpreter, so these things work as before: the unlock tree, op/tick costs, drones,
breakpoints and step-by-step mode. `.py` files are not translated: the game parses them itself.

```java
void main() {
    while (true) {
        for (int i = 0; i < getWorldSize(); i++) {
            if (canHarvest()) {
                harvest();
            }
            plant(Entities.Bush);
            move(North);
        }
        move(East);
    }
}
```

What the mod changes in the game (each window follows its own language):

- Code windows take the language of their extension. Click a window's name to rename it: `main.java` → `main.rs` makes
  it Rust (the code is not converted: you rewrite it). A name typed without an extension keeps the window's extension;
  a new window (`f0`, `f1` ...) gets the default language's extension. Extensions: `.java`, `.py`, `.c`, `.cpp`
  (`.cc`, `.cxx`), `.cs`, `.rs`, `.zig`, `.js` (`.mjs`), `.R` (`.r`). Two files cannot have the same name with
  different extensions (`a.java` and `a.rs`): other files use a file by its name without the extension.
- Files call each other across languages, in every language: `Utils.harvestRow()` in Java works when `Utils` is a
  Python, Rust or C file, `mod helper;` + `helper::twice(21)` in Rust can call a Java file, and
  `import * as pyhelp from "./pyhelp.py";` in JavaScript can call a Python file. Values from Python, JavaScript and R
  files have no static type (`Object` in Java, `var`/`auto` style escape hatches elsewhere: cast them). Each
  language spells the import its own way (docs/LANGUAGE-GUIDE.md §9 and `docs/languages/<id>.md`).
- Compile errors show in the editor, at the right position, with messages in the style of the real compiler.
- Syntax colours, auto-indent and un-indent (`{` / `}`), the language's names in code completion and hover tooltips
  (`canHarvest` in Java and JavaScript, `CanHarvest` in C#, `can_harvest` in C, Rust and R ...).
- A new code window starts with the language's template (`void main() { }` in Java, `fn main() { }` in Rust ...).
- In-game docs, tooltips and unlock boxes show the default language: hand-written pages from `content/<language>/` when
  the language has them, else the game's pages with every code example translated automatically. Hover tooltips in a
  code window use that window's language.
- Runtime errors name the language's functions, and printed values look like the language prints them (`true`/`True`/`TRUE`, `null`/`None`/`nullptr` ...).
- Every language costs the same unlocks as the Python it replaces. The code a language needs for its structure (`void main()`, `fn main()`, `#include`, class wrappers) costs no unlocks.
- A **Mods** button on the title page and in the pause menu (see below), with the default language and two optional
  content extensions: **Cybersecurity** (crack vaults, break ciphers, defend the farm network; currency Bits) and
  **AI / ML** (regression, classifiers, k-means, neural nets, Q-learning; currency Insight). Each adds its own branch
  to the unlock tree, new builtins in every language, docs pages and a capstone farm bonus.
- **Leaderboard score uploads are disabled** while the mod is active, in every language (Python too). Leaderboard runs still work and the leaderboards still show, but your modded scores are not sent to Steam.

The Java language subset and the game API are described in [docs/SPEC.md](docs/SPEC.md); the other languages in
[docs/SPEC-v2.md](docs/SPEC-v2.md) (and `docs/languages/<id>.md` where present).

## The Mods menu

Title page or pause menu (Esc) > **Mods**:

- **default language**: click to cycle through the languages this build has. It is the language of new files and of
  the docs, string tooltips and unlock boxes. Your existing files never change: each keeps the language of its extension.
- One row per content extension (**Cybersecurity**, **AI / ML**): Enabled / Disabled, with a one-line description.
  Extensions are off by default; their progress is stored per save and comes back when you turn an extension on
  again. See [docs/EXTENSIONS.md](docs/EXTENSIONS.md).
- **apply** asks for confirmation, saves and reloads. **Back** discards changes you did not apply.

## Install

The mod is a [BepInEx 5](https://github.com/BepInEx/BepInEx) plugin. The game is a Windows (Unity, Mono) build. On Linux it runs through Proton.

### With the install script (Linux, or Windows with Git Bash/WSL)

```sh
scripts/install.sh "/path/to/steamapps/common/The Farmer Was Replaced"
```

The script:

1. Installs BepInEx 5.4.23.6 (Windows x64) into the game folder if BepInEx is not there yet. It checks the SHA-256 of the download.
2. Copies the mod to `BepInEx/plugins/JavaFarmer/`. If `dist/` has no package, it builds one first (see "Build from source").

Use `--dry-run` to see what the script would do without changes.

### By hand (Windows)

1. Download `BepInEx_win_x64_5.4.23.6.zip` from the [BepInEx releases](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6).
   Extract it into the game folder (the folder that contains `TheFarmerWasReplaced.exe`).
2. Extract the JavaFarmer zip into the same folder. You get `BepInEx/plugins/JavaFarmer/`.
3. Start the game.

### Linux (Steam + Proton)

Do the steps above (or run the script). Then in Steam, right-click the game > Properties > General > Launch Options and set:

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

If you do not set this option, Wine does not load BepInEx and the game stays Python.

### Check that it works

- `BepInEx/LogOutput.log` contains a line like `JavaFarmer 0.1.0: N patches applied. Default language: Java.`
- The title page has a **Mods** button, and a new code window shows `void main() {` (in Java).

### Settings

`BepInEx/config/javafarmer.plugin.cfg` is created after the first start:

- `[General] Language = java`: the default language (`java`, `python`, `c`, `cpp`, `rust`, `js`, `zig`, `csharp`, `r`)
  for new files and the docs. Same as the Mods menu row.
- `[General] Enabled = true`. Set it to `false` to play vanilla Python without uninstalling. Then the mod patches nothing.
- `[General] ContentForAllLanguages = true`. The mod's docs are in English only. Set it to `false` to use them only when the game language is English.
- `[Extensions] cybersecurity / ai_ml = false`: the content extensions (the Mods menu changes these).

## Your save files

The mod uses the same save slots. Code files are saved with their real extension (`main.java`, `utils.rs`, `bot.py`),
so you can open the save folder in VS Code or any editor (with the game's "File Watcher" option on, saved changes show
up in the game). Each save folder also gets `javafarmer.json` (the mod's data: format version, progress of the content
extensions) and, next to `__builtins__.py`, an API file for every language used in the save (`JavaFarmerApi.java`,
`farm.h`, `farm.rs`, `Farm.cs` ...) for external editors. Build files of the real toolchains can live in the save
folder too: `build.zig` and `build.rs` are never loaded as code windows (so no window can be named `build` in Zig or
Rust), and files without a code extension (`Cargo.toml`, `package.json`, `CMakeLists.txt`) are ignored anyway.

Older saves: a save made with JavaFarmer before this version kept its Java code in `.py` files. On the first load
those files are renamed to `.java` (their code does not change; the old `.py` files are copied to
`javafarmer-backup/rename-<date-time>/` in the save folder). A file that is valid Python but not Java stays `.py`.
A save of the unmodded game stays Python: its `.py` files keep working, and you can add files in other languages next
to them. **Back up your save before you use the mod.**

## Uninstall

1. Delete `BepInEx/plugins/JavaFarmer/` from the game folder (or set `Enabled = false`).
2. To remove BepInEx too, also delete `BepInEx/`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `changelog.txt`.
3. On Linux, remove the `WINEDLLOVERRIDES` launch option.

## Single-exe build

`scripts/build-exe.sh` makes `dist/JavaFarmer.exe`: one Windows exe with the whole game, BepInEx and the mod inside.
You can add it to Steam as a non-Steam game (Linux with Proton, or Windows). See [docs/STEAM.md](docs/STEAM.md).

```sh
scripts/build-exe.sh            # runs package.sh, then builds dist/JavaFarmer.exe
```

Requirements: the same as "Build from source", plus Go 1.23 or later, `curl` and `unzip`. To give the exe the
game's icon and version info, also install `llvm-windres` (LLVM) or `x86_64-w64-mingw32-windres` (MinGW).
Without one of them, the build continues without an icon.

The script copies the game files from the repo root (or `GAME_DIR`) into a temp folder, installs the official
BepInEx 5.4.23.6 and the current `dist/` package into that copy, and appends the copy (zip and SHA-256 manifest)
to a small Go launcher (`src/JavaFarmer.Launcher`). The same inputs give the same exe, byte for byte.

Unity cannot run from inside an exe. So on the first start the launcher unpacks the game to
`%LOCALAPPDATA%\JavaFarmer\`. On every start it checks those files and repairs them, then it starts the game.
Details: [docs/STEAM.md](docs/STEAM.md).

## Build from source

Requirements: .NET SDK 8, bash. The build needs the game's DLLs from `TheFarmerWasReplaced_Data/Managed`
(compile-time only; they are not packaged).

```sh
# game copy in the repo root (default), or point GAME_DIR at your install
GAME_DIR="/path/to/The Farmer Was Replaced" scripts/package.sh
```

Output: `dist/JavaFarmer/` (copy its `BepInEx` folder into the game folder) and `dist/JavaFarmer-<version>.zip`.
`DIST_DIR`, `ARTIFACTS_PATH` and `JAVAFARMER_COMPILER_PROJECT` change where the package goes, where dotnet builds and
which compiler project it builds against (see the top of `scripts/package.sh`).

Layout:

| Path | What |
| --- | --- |
| `src/JavaFarmer.Compiler` | The languages (frontends, printers, editor services) and the shared IR to game-Python emitter (no game references) |
| `src/JavaFarmer.Plugin` | BepInEx plugin: Harmony patches, editor support, parse bridge, Mods menu, save conversion, extensions |
| `content/<language>/` | Docs (`docs/`), strings (`Strings/`) and unlock-box code (`unlock_display_code.txt`) per language; `content/python/` holds the canonical texts of the extensions |
| `tests/` | xUnit tests. The plugin tests run the game's real parser headless (`dotnet test`) |
| `src/JavaFarmer.Launcher` | Go launcher for the single-exe build (`go test ./...` runs on Linux) |
| `scripts/` | `package.sh`, `install.sh`, `build-exe.sh` |

## Known limitations

- Java subset only: static methods and fields, no objects of your own classes, no interfaces/enums/records,
  no exceptions (`try`/`throw`), no `char`, no bitwise operators, no labeled `break`. See [docs/SPEC.md](docs/SPEC.md) §3 and §12.
- Numbers: the game has one number type. `int` and `double` are checked at compile time, but `int` does not overflow,
  and printed numbers use the game's format, not Java's (`2.0` prints as `2`, at most two decimals).
- Other values print like Java: `true`, `null`, `[1, 2]` for lists, arrays and sets, `{a=1, b=2}` for maps,
  `Position[x=1, y=2]` and `Companion[entity=Entities.Bush, position=Position[x=1, y=2]]` (record style), `Drone@3` for a drone.
  This applies to `System.out.println`, `quickPrint`, `String.valueOf`/`"" + x`, the output page, error messages and the
  value tooltip of a hovered variable.
- The docs are English only. Languages without hand-written docs get the game's pages with the code examples
  translated automatically; an example that cannot be translated stays in Python.
- The mod never converts your code between languages: changing a file's extension changes how it is compiled, not its text.
- A game update can change the methods the mod patches. If a required patch fails, the mod removes all its
  patches and the game runs vanilla Python (see `BepInEx/LogOutput.log`).
- Leaderboard uploads are disabled (see above).
- A float turned into text with the game's `str()` (JavaScript `"mse " + x`, for example) can show 17 significant
  digits (`5.5481281663516073`) where the real language prints the shortest form (`5.548128166351607`).
- Unlock boxes in the tree are sized for the game's short Python snippets; a longer snippet of another language
  (`HashMap::from([(1, 2)])`) can run past the edge of its box.
