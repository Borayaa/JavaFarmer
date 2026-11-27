# JavaFarmer content

The in-game texts of The Farmer Was Replaced for each programming language (SPEC §10, SPEC-v2 §5). The English
source texts are CC0 (`Languages/LICENSE`).

## Layout (one folder per language id: java, python, c, cpp, rust, js, zig, csharp, r)
- `<lang>/docs/**/*.md` – pages with the same relative paths as `Languages/EN/docs`, so the game's links keep working.
  `content/java/docs` has all 48 pages in Java.
- `<lang>/Strings/*.txt` – string overrides, same `@key = value` format as `Languages/EN/Strings` (only changed or new keys).
- `<lang>/unlock_display_code.txt` – the code shown in unlock boxes (`UnlockSO.displayCode`): a line `@<unlockName>`,
  then the snippet on the following line(s) until the next `@<name>` line (`@name = snippet` on one line works too).
  A code line may start with `@` (Zig: `const std = @import("std");` or `@import("std")`); only `@` + a bare name is a
  header. To start a code line with `@name` alone, write `@@name`.
- `python/` – the canonical (Python) texts of the content extensions: `python/docs/mods/<extension>/*.md`,
  `python/Strings/*.txt`, optional `python/docs/mods/home_section.md` (appended to the docs home page in every language).
- `<lang>/_samples.txt`, `README.md` – authoring files, not packaged.

## How the plugin picks a text (ContentStore)
For the default language L (Mods menu; hover tooltips in a code window use that window's language instead):
- docs: `L/docs/<path>` → else `python/docs/<path>` with its code spans translated to L (DocsTranslator) → else the
  game's own page, its code spans translated to L. Python mode: `python/` and the game's pages only.
- strings: `python/Strings` (code spans translated to L) overlaid by `L/Strings`; game tooltips that L does not
  override (code_tooltips, unlock_tooltips, item_tooltips) get their code spans translated.
- unlock boxes: `L/unlock_display_code.txt` → else the game's Python snippet printed in L.
- A language whose printer cannot print code yet keeps the Python text instead of a translation.

## Java files (content/java)
- `docs/**/*.md` – one file for every `Languages/EN/docs/**/*.md` (48 files).
- `Strings/*.txt` – overrides only for keys whose text shows Python code or Python builtin names. Files: `code_tooltips.txt`, `unlock_tooltips.txt`, `parse_errors.txt`, `execute_errors.txt`, `warnings.txt`, `other.txt`, `item_tooltips.txt`.
- `unlock_display_code.txt` – only unlocks that have a non-empty displayCode in the game are listed (16).
- `_samples.txt` – every code sample from the files above, with its source and kind, for compile-checking (format described at its top).

## Conventions
- Game markdown dialect kept: single backticks for all code (also multi-line), `<unlock=x>…</unlock>`, `<spoiler=…>…</spoiler>`, `{{…}}` and `{0}` placeholders verbatim, `![](Sprite)` images.
- Code never contains `{{` or `}}` (the game treats `{{.*}}` as a placeholder), `](`, `![`, or a line starting with `#`. A heading never follows an unlock/spoiler tag on the same line (headings must start a line).
- Strings: no value line starts with `@`; formatted error strings (`parse_errors`, `execute_errors`, `warnings`, `other`) contain no braces except `{n}` placeholders.
- Java API exactly as SPEC §3-§5: lowerCamelCase methods (`canHarvest()`), game constants unchanged (`Entities.Carrot`, `North`, `Items.Hay`), `System.out.println`, arrays, `ArrayList`/`HashMap`/`HashSet`, `List.of`/`Map.of`/`Set.of`, `measure()` cast from `Object`, `getCompanion()` null-checked, lambdas and method references for `spawnDrone`.
- Full programs use a compact source file with `void main()`; snippets after `first_program.md` may omit the wrapper. Library files use the class form `public class Utils { static ... }` and are called as `Utils.plantRow()`.
- Not used anywhere: char literals, try/catch, instance classes, bitwise operators, labels, `instanceof`, `long`/`float`, `++`/`--` inside expressions, `Math.round`.
- Function pages link targets stay `functions/<python_name>` (game-generated pages); only the link text is Java.
- `code_tooltips.txt` keeps the original keys (`code_tooltip_<python_name>`) and adds keys for Java words: `true false null void int double boolean String var static final new do switch global import`. The plugin decides which hovered Java word maps to which key.
- Images that showed Python code were replaced by Java text: `ImportsInOnePicture400` (import.md) and `LeaderboardSetup400` (leaderboard.md).
