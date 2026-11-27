#!/usr/bin/env bash
# Installs JavaFarmer into a copy of The Farmer Was Replaced.
#
#   scripts/install.sh [--dry-run] [--force] <game dir>
#
# <game dir> is the folder that contains TheFarmerWasReplaced.exe and TheFarmerWasReplaced_Data/.
# - Installs BepInEx 5 (Windows x64, pinned release, checksum verified) if the game has no BepInEx yet.
# - Copies the plugin from dist/ (or DIST_DIR=<dir>; runs scripts/package.sh first if needed).
# - BEPINEX_ZIP=<file> uses an already downloaded BepInEx zip (its checksum is still verified).
# On Linux the game runs through Proton: see the launch option printed at the end.
set -euo pipefail

BEPINEX_VERSION="5.4.23.6"
BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v${BEPINEX_VERSION}/BepInEx_win_x64_${BEPINEX_VERSION}.zip"
BEPINEX_SHA256="6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DRY_RUN=0
FORCE=0
GAME=""

usage() { sed -n '2,11p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dry-run) DRY_RUN=1 ;;
    --force) FORCE=1 ;;
    -h|--help) usage; exit 0 ;;
    -*) echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
    *) GAME="$1" ;;
  esac
  shift
done

if [[ -z "$GAME" ]]; then usage >&2; exit 2; fi
if [[ ! -d "$GAME/TheFarmerWasReplaced_Data" ]]; then
  echo "error: $GAME does not look like the game folder (TheFarmerWasReplaced_Data/ not found)" >&2
  exit 1
fi
GAME="$(cd "$GAME" && pwd)"
if [[ "$GAME" == "$ROOT" && $FORCE -eq 0 ]]; then
  echo "error: refusing to install into the repository's own game copy (use --force if you really mean it)" >&2
  exit 1
fi

run() {
  if [[ $DRY_RUN -eq 1 ]]; then echo "[dry-run] $*"; else "$@"; fi
}

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1
  else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

unzip_into() { # zip, destination
  if command -v unzip >/dev/null 2>&1; then
    unzip -q -o "$1" -d "$2"
  else
    python3 -I -m zipfile -e "$1" "$2"
  fi
}

# 1. BepInEx
if [[ -f "$GAME/BepInEx/core/BepInEx.dll" ]]; then
  echo "==> BepInEx already installed in $GAME"
else
  echo "==> Installing BepInEx $BEPINEX_VERSION (Windows x64) into $GAME"
  if [[ $DRY_RUN -eq 1 ]]; then
    echo "[dry-run] download $BEPINEX_URL"
    echo "[dry-run] verify sha256 $BEPINEX_SHA256"
    echo "[dry-run] extract into $GAME"
  else
    TMP="$(mktemp -d)"
    trap 'rm -rf "$TMP"' EXIT
    if [[ -n "${BEPINEX_ZIP:-}" ]]; then cp "$BEPINEX_ZIP" "$TMP/bepinex.zip"
    else curl -fsSL --retry 3 -o "$TMP/bepinex.zip" "$BEPINEX_URL"; fi
    ACTUAL="$(sha256_of "$TMP/bepinex.zip")"
    if [[ "$ACTUAL" != "$BEPINEX_SHA256" ]]; then
      echo "error: BepInEx download checksum mismatch ($ACTUAL)" >&2
      exit 1
    fi
    unzip_into "$TMP/bepinex.zip" "$GAME"
  fi
fi

# 2. Plugin
SRC="${DIST_DIR:-$ROOT/dist}/JavaFarmer/BepInEx/plugins/JavaFarmer"
if [[ ! -f "$SRC/JavaFarmer.Plugin.dll" ]]; then
  echo "==> No package in dist/: building it"
  run "$ROOT/scripts/package.sh"
fi
echo "==> Installing JavaFarmer into $GAME/BepInEx/plugins/JavaFarmer"
run mkdir -p "$GAME/BepInEx/plugins"
run rm -rf "$GAME/BepInEx/plugins/JavaFarmer"
run cp -R "$SRC" "$GAME/BepInEx/plugins/JavaFarmer"

cat <<EOF

JavaFarmer is installed.

Windows: start the game from Steam as usual.

Linux (Steam + Proton): BepInEx loads through winhttp.dll, which Wine ignores unless told otherwise.
In Steam: right-click The Farmer Was Replaced > Properties > General > Launch Options, and set:

    WINEDLLOVERRIDES="winhttp=n,b" %command%

The first start with BepInEx takes a little longer. The log is in BepInEx/LogOutput.log.
Each code file's language is its extension (main.java, utils.rs, bot.py ...): rename a code window to
change it. The default language for new files and the docs, and the content extensions, are in the
game: title page or pause menu > Mods. Your code is never converted.
Settings (General.Language, General.Enabled, Extensions.*): BepInEx/config/javafarmer.plugin.cfg
(created after the first start). Leaderboard score uploads are disabled while the mod is active.
EOF
