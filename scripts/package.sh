#!/usr/bin/env bash
# Builds JavaFarmer in Release and lays out a ready-to-copy mod folder:
#   dist/JavaFarmer/BepInEx/plugins/JavaFarmer/{JavaFarmer.Plugin.dll, JavaFarmer.Compiler.dll, content/<lang>/...}
#   dist/JavaFarmer-<version>.zip  (extract into the game folder, next to TheFarmerWasReplaced.exe)
#
# The build needs the game's Managed DLLs (compile-time only, they are never packaged).
# By default they are read from the repo root; set GAME_DIR=/path/to/game to use another install.
# Optional environment:
#   DIST_DIR=<dir>                       write the folder and zip there instead of dist/
#   ARTIFACTS_PATH=<dir>                 dotnet --artifacts-path (keeps parallel builds apart)
#   JAVAFARMER_COMPILER_PROJECT=<csproj> build against another copy of the compiler
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/src/JavaFarmer.Plugin/JavaFarmer.Plugin.csproj"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJ" | head -n1)"
VERSION="${VERSION:-0.0.0}"

BUILD_ARGS=(-c Release --nologo)
if [[ -n "${GAME_DIR:-}" ]]; then
  BUILD_ARGS+=("-p:GameDir=${GAME_DIR%/}/")
fi
if [[ -n "${JAVAFARMER_COMPILER_PROJECT:-}" ]]; then
  BUILD_ARGS+=("-p:JavaFarmerCompilerProject=${JAVAFARMER_COMPILER_PROJECT}")
fi
if [[ -n "${ARTIFACTS_PATH:-}" ]]; then
  BUILD_ARGS+=(--artifacts-path "${ARTIFACTS_PATH}")
fi
if [[ ! -f "${GAME_DIR:-$ROOT}/TheFarmerWasReplaced_Data/Managed/Core.dll" ]]; then
  echo "error: game DLLs not found in ${GAME_DIR:-$ROOT}/TheFarmerWasReplaced_Data/Managed (set GAME_DIR to your game folder)" >&2
  exit 1
fi

echo "==> Building JavaFarmer $VERSION (Release)"
dotnet build "$PROJ" "${BUILD_ARGS[@]}"

if [[ -n "${ARTIFACTS_PATH:-}" ]]; then
  OUT="${ARTIFACTS_PATH}/bin/JavaFarmer.Plugin/release"
else
  OUT="$ROOT/src/JavaFarmer.Plugin/bin/Release/netstandard2.1"
fi
DIST_ROOT="${DIST_DIR:-$ROOT/dist}"
DIST="$DIST_ROOT/JavaFarmer"
PLUGIN_DIR="$DIST/BepInEx/plugins/JavaFarmer"

rm -rf "$DIST"
mkdir -p "$PLUGIN_DIR"
cp "$OUT/JavaFarmer.Plugin.dll" "$OUT/JavaFarmer.Compiler.dll" "$PLUGIN_DIR/"

# content/<lang>/{docs,Strings,unlock_display_code.txt} per language; content/python = canonical extension texts
if [[ -d "$ROOT/content" ]]; then
  cp -R "$ROOT/content" "$PLUGIN_DIR/content"
  # authoring files (samples lists, notes) are not needed in the game
  find "$PLUGIN_DIR/content" \( -name 'README.md' -o -name '_*' \) -type f -delete
  echo "    content languages: $(cd "$PLUGIN_DIR/content" && ls -d */ 2>/dev/null | tr -d '/' | tr '\n' ' ')"
else
  echo "warning: $ROOT/content does not exist: the package has no docs/strings for the other languages (the game shows its Python docs)" >&2
fi
[[ -f "$ROOT/README.md" ]] && cp "$ROOT/README.md" "$PLUGIN_DIR/README.md"

ZIP="$DIST_ROOT/JavaFarmer-$VERSION.zip"
rm -f "$ZIP"
if command -v zip >/dev/null 2>&1; then
  (cd "$DIST" && zip -qr "$ZIP" BepInEx)
else
  (cd "$DIST" && python3 -I -c 'import sys, zipfile, os
with zipfile.ZipFile(sys.argv[1], "w", zipfile.ZIP_DEFLATED) as z:
    for d, _, fs in os.walk("BepInEx"):
        for f in fs: z.write(os.path.join(d, f))' "$ZIP")
fi

echo "==> Done"
echo "    folder: $DIST"
echo "    zip:    $ZIP"
