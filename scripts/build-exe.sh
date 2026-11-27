#!/usr/bin/env bash
# Builds dist/JavaFarmer.exe: one Windows exe that holds the whole game with BepInEx and JavaFarmer.
#
#   scripts/build-exe.sh [--skip-package]
#
# Steps: package.sh (current mod) -> fresh copy of the game files in a temp folder -> install.sh
# (official BepInEx 5.4.23.6 + dist/) -> manifest + zip -> Go launcher (src/JavaFarmer.Launcher)
# -> launcher + zip + trailer = dist/JavaFarmer.exe.
#
# The game files are read from the repo root (or GAME_DIR). Requirements: bash, go >= 1.23,
# curl, unzip (or python3), the .NET SDK (for package.sh). Optional: llvm-windres or
# x86_64-w64-mingw32-windres, for the game icon and version info on the exe.
# Temp files go to ${TMPDIR:-/tmp}. SOURCE_DATE_EPOCH sets the zip timestamps (default 2020-01-01).
# DIST_DIR / ARTIFACTS_PATH / JAVAFARMER_COMPILER_PROJECT are passed on to package.sh (see there);
# the exe is written to ${DIST_DIR:-dist}/JavaFarmer.exe.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LAUNCHER="$ROOT/src/JavaFarmer.Launcher"
GAME_SRC="${GAME_DIR:-$ROOT}"
GAME_SRC="${GAME_SRC%/}"
DIST_ROOT="${DIST_DIR:-$ROOT/dist}"
export DIST_DIR="$DIST_ROOT"
OUT="$DIST_ROOT/JavaFarmer.exe"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/JavaFarmer.Plugin/JavaFarmer.Plugin.csproj" | head -n1)"
VERSION="${VERSION:-0.0.0}"
SKIP_PACKAGE=0

for arg in "$@"; do
  case "$arg" in
    --skip-package) SKIP_PACKAGE=1 ;;
    -h|--help) sed -n '2,16p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

# Game files copied into the exe. Nothing else from the game folder is used.
GAME_FILES=(TheFarmerWasReplaced.exe UnityPlayer.dll UnityCrashHandler64.exe TheFarmerWasReplaced_Data MonoBleedingEdge D3D12)
for f in "${GAME_FILES[@]}"; do
  if [[ ! -e "$GAME_SRC/$f" ]]; then
    echo "error: $GAME_SRC/$f not found (set GAME_DIR to the game folder)" >&2
    exit 1
  fi
done

# Host-side Go tools must not inherit a cross-compile target from the caller's shell.
host_go() { env -u GOOS -u GOARCH CGO_ENABLED=0 go "$@"; }

WORK="$(mktemp -d "${TMPDIR:-/tmp}/javafarmer-exe.XXXXXX")"
SYSO="$LAUNCHER/rsrc_windows_amd64.syso"
cleanup() { rm -rf "$WORK"; rm -f "$SYSO"; }
trap cleanup EXIT

echo "==> Testing the launcher"
(cd "$LAUNCHER" && host_go vet ./... && env GOOS=windows GOARCH=amd64 go vet ./... && host_go test ./...)

if [[ $SKIP_PACKAGE -eq 0 ]]; then
  GAME_DIR="$GAME_SRC" "$ROOT/scripts/package.sh"
fi
if [[ ! -f "$DIST_ROOT/JavaFarmer/BepInEx/plugins/JavaFarmer/JavaFarmer.Plugin.dll" ]]; then
  echo "error: dist/JavaFarmer is missing (run scripts/package.sh)" >&2
  exit 1
fi

echo "==> Staging a fresh game copy in $WORK/game"
STAGE="$WORK/game"
mkdir -p "$STAGE"
for f in "${GAME_FILES[@]}"; do
  cp -R "$GAME_SRC/$f" "$STAGE/"
done
rm -rf "$STAGE/TheFarmerWasReplaced_Data/StreamingAssets/Languages/.git"
rm -f "$STAGE/TheFarmerWasReplaced_Data/Plugins/x86_64/"*.bak
EMU_CFG="$STAGE/TheFarmerWasReplaced_Data/Plugins/x86_64/steam_settings/configs.main.ini"
if [[ -f "$EMU_CFG" ]]; then
  # the Steam emulator must not open network ports
  sed -i 's/^disable_networking=.*/disable_networking=1/' "$EMU_CFG"
  grep -q '^disable_networking=1' "$EMU_CFG" || printf '[main::connectivity]\ndisable_networking=1\n' >> "$EMU_CFG"
fi

echo "==> Installing BepInEx + JavaFarmer into the staged copy"
"$ROOT/scripts/install.sh" "$STAGE" | sed -n '1,/^JavaFarmer is installed/p'
[[ -f "$STAGE/BepInEx/core/BepInEx.dll" && -f "$STAGE/winhttp.dll" ]] || { echo "error: BepInEx was not installed" >&2; exit 1; }

WINDRES="$(command -v llvm-windres || command -v x86_64-w64-mingw32-windres || true)"
if [[ -n "$WINDRES" ]]; then
  echo "==> Adding the game icon and version info ($(basename "$WINDRES"))"
  RES="$WORK/res"
  mkdir -p "$RES"
  (cd "$LAUNCHER" && host_go run ./cmd/jfres -exe "$STAGE/TheFarmerWasReplaced.exe" -ico "$RES/icon.ico")
  IFS=. read -r V1 V2 V3 _ <<<"$VERSION.0.0.0"
  cat > "$RES/app.manifest" <<EOF
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <assemblyIdentity type="win32" name="JavaFarmer.Launcher" version="$V1.$V2.$V3.0" processorArchitecture="amd64"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security><requestedPrivileges><requestedExecutionLevel level="asInvoker" uiAccess="false"/></requestedPrivileges></security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application><supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/></application>
  </compatibility>
</assembly>
EOF
  cat > "$RES/app.rc" <<EOF
1 ICON "icon.ico"
1 24 "app.manifest"
1 VERSIONINFO
FILEVERSION $V1,$V2,$V3,0
PRODUCTVERSION $V1,$V2,$V3,0
FILEOS 0x40004
FILETYPE 0x1
BEGIN
  BLOCK "StringFileInfo"
  BEGIN
    BLOCK "040904b0"
    BEGIN
      VALUE "FileDescription", "The Farmer Was Replaced with JavaFarmer"
      VALUE "FileVersion", "$VERSION"
      VALUE "InternalName", "JavaFarmer"
      VALUE "OriginalFilename", "JavaFarmer.exe"
      VALUE "ProductName", "JavaFarmer"
      VALUE "ProductVersion", "$VERSION"
    END
  END
  BLOCK "VarFileInfo"
  BEGIN
    VALUE "Translation", 0x409, 1200
  END
END
EOF
  (cd "$RES" && "$WINDRES" --target=x86_64-w64-mingw32 -O coff -i app.rc -o "$SYSO")
else
  echo "warning: no llvm-windres/x86_64-w64-mingw32-windres: the exe gets no icon or version info" >&2
fi

echo "==> Building the launcher (windows/amd64)"
(cd "$LAUNCHER" && env CGO_ENABLED=0 GOOS=windows GOARCH=amd64 go build -trimpath -buildvcs=false \
  -ldflags "-s -w -H=windowsgui -buildid=" -o "$WORK/launcher.exe" .)

echo "==> Packing"
mkdir -p "$DIST_ROOT"
(cd "$LAUNCHER" && host_go run ./cmd/jfpack -stage "$STAGE" -launcher "$WORK/launcher.exe" -out "$OUT" \
  -release "$VERSION" -mtime "${SOURCE_DATE_EPOCH:-1577836800}")

SIZE="$(stat -c %s "$OUT" 2>/dev/null || stat -f %z "$OUT")"
SHA="$(sha256sum "$OUT" 2>/dev/null | cut -d' ' -f1 || shasum -a 256 "$OUT" | cut -d' ' -f1)"
echo "==> Done"
echo "    exe:    $OUT"
echo "    size:   $SIZE bytes ($((SIZE / 1048576)) MiB)"
echo "    sha256: $SHA"
echo "    Add it to Steam: docs/STEAM.md"
