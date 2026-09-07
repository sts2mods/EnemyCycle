#!/usr/bin/env bash
# Build EnemyCycle.dll and install it to the game's mods/ folder.
# Usage: ./build.sh [Release|Debug]   (default Release)

set -euo pipefail
cd "$(dirname "$0")"

CONFIG="${1:-Release}"

GAME_DIR="${STS2_GAME_DIR:-}"
if [[ -z "$GAME_DIR" ]]; then
  case "$(uname -s)" in
    Darwin*)
      GAME_DIR="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2"
      ;;
    Linux*)
      for candidate in \
        "$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2" \
        "/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2" \
        "/mnt/c/Program Files/Steam/steamapps/common/Slay the Spire 2" \
        "/mnt/d/SteamLibrary/steamapps/common/Slay the Spire 2"; do
        if [[ -d "$candidate" ]]; then
          GAME_DIR="$candidate"
          break
        fi
      done
      ;;
  esac
fi

if [[ -z "$GAME_DIR" ]] || [[ ! -d "$GAME_DIR" ]]; then
  echo "ERROR: Slay the Spire 2 was not found. Set STS2_GAME_DIR to the game directory." >&2
  exit 1
fi

if [[ -d "$GAME_DIR/data_sts2_windows_x86_64" ]]; then
  GAME_DATA_DIR="$GAME_DIR/data_sts2_windows_x86_64"
elif [[ -d "$GAME_DIR/data_sts2_linuxbsd_x86_64" ]]; then
  GAME_DATA_DIR="$GAME_DIR/data_sts2_linuxbsd_x86_64"
elif [[ -d "$GAME_DIR/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64" ]]; then
  GAME_DATA_DIR="$GAME_DIR/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64"
else
  echo "ERROR: Slay the Spire 2 assemblies were not found under: $GAME_DIR" >&2
  exit 1
fi

case "$(uname -s)" in
  Darwin*)  MODS_DIR="$GAME_DIR/SlayTheSpire2.app/Contents/MacOS/mods" ;;
  Linux*)   MODS_DIR="$GAME_DIR/mods" ;;
  *)        MODS_DIR="$GAME_DIR/mods" ;;
esac

OUT_DIR="$PWD/out/EnemyCycle"
INSTALL_DIR="$MODS_DIR/EnemyCycle"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: dotnet not found. Install .NET 9 SDK: https://dotnet.microsoft.com/download/dotnet/9.0" >&2
  exit 1
fi

echo "=== Building EnemyCycle ($CONFIG) ==="
echo "Game dir:   $GAME_DIR"
echo "Game data:  $GAME_DATA_DIR"
echo "Build out:  $OUT_DIR"
echo "Install to: $INSTALL_DIR"
echo

rm -rf "$OUT_DIR"
dotnet build EnemyCycle.csproj -c "$CONFIG" -o "$OUT_DIR" \
  -p:STS2GameDir="$GAME_DIR" \
  -p:STS2GameDataDir="$GAME_DATA_DIR"

echo
echo "=== Installing ==="
mkdir -p "$INSTALL_DIR"
cp "$OUT_DIR/EnemyCycle.dll" "$INSTALL_DIR/"
cp mod_manifest.json "$INSTALL_DIR/"
echo "Installed:"
ls -la "$INSTALL_DIR/"
echo
echo "Done. Launch STS2 to load the mod."
