#!/usr/bin/env sh
set -eu

game_dir='D:/SteamLibrary/steamapps/common/Gamble With Your Friends'
version='0.2.3'

usage() {
    printf '%s\n' \
        'Usage: sh scripts/package-release.sh [--game-dir DIR] [--version VERSION]' \
        '' \
        'Builds the plugin and writes dist/GwyfJpn-v<version>.zip with:' \
        '  GwyfJpn/GwyfJpn.Plugin.dll' \
        '  GwyfJpn/GwyfJpn.Core.dll' \
        '  GwyfJpn/config/display_sinks.json' \
        '  GwyfJpn/fonts/*' \
        '  GwyfJpn/translations/ja/translations.ja.json' \
        '' \
        'Extract the zip into the game''s BepInEx/plugins/ directory.'
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --game-dir|-g|-GameDir)
            if [ "$#" -lt 2 ]; then usage >&2; exit 2; fi
            game_dir=$2
            shift 2
            ;;
        --game-dir=*)
            game_dir=${1#*=}
            shift
            ;;
        --version|-v|-Version)
            if [ "$#" -lt 2 ]; then usage >&2; exit 2; fi
            version=$2
            shift 2
            ;;
        --version=*)
            version=${1#*=}
            shift
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            printf 'Unknown argument: %s\n' "$1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

script_path=$(printf '%s' "$0" | tr '\\' '/')
script_dir=$(CDPATH= cd -- "$(dirname -- "$script_path")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/.." && pwd)

sh "$script_dir/build.sh" --game-dir "$game_dir" --plugin

plugin_out=$repo_root/src/GwyfJpn.Plugin/bin/Release/netstandard2.1
dist_dir=$repo_root/dist
stage_dir=$dist_dir/GwyfJpn
zip_path=$dist_dir/GwyfJpn-v$version.zip
translation_file=$repo_root/translations/ja/translations.ja.json

if [ ! -f "$plugin_out/GwyfJpn.Plugin.dll" ]; then
    printf 'Plugin build output not found: %s\n' "$plugin_out/GwyfJpn.Plugin.dll" >&2
    exit 1
fi

if [ ! -f "$translation_file" ]; then
    printf 'Translation file not found: %s\n' "$translation_file" >&2
    exit 1
fi

rm -rf "$stage_dir"
mkdir -p "$stage_dir/translations/ja"

cp -f "$plugin_out/GwyfJpn.Plugin.dll" "$stage_dir/"
cp -f "$plugin_out/GwyfJpn.Core.dll" "$stage_dir/"
cp -R "$plugin_out/config" "$stage_dir/"

if [ -d "$repo_root/fonts" ]; then
    mkdir -p "$stage_dir/fonts"
    cp -R "$repo_root/fonts/." "$stage_dir/fonts/"
fi

cp -f "$translation_file" "$stage_dir/translations/ja/translations.ja.json"

rm -f "$zip_path"
(
    cd "$dist_dir"
    if command -v zip >/dev/null 2>&1; then
        zip -r "GwyfJpn-v$version.zip" GwyfJpn
    elif command -v python3 >/dev/null 2>&1; then
        python3 - "$zip_path" "$stage_dir" <<'PY'
import sys
import zipfile
from pathlib import Path

zip_path = Path(sys.argv[1])
root = Path(sys.argv[2])
with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(root.rglob("*")):
        if path.is_file():
                archive.write(path, path.relative_to(root.parent).as_posix())
PY
    elif command -v powershell.exe >/dev/null 2>&1; then
        ps_zip_path=$zip_path
        ps_stage_dir=$stage_dir
        if command -v cygpath >/dev/null 2>&1; then
            ps_zip_path=$(cygpath -w "$zip_path")
            ps_stage_dir=$(cygpath -w "$stage_dir")
        fi

        ZIP_PATH=$ps_zip_path STAGE_DIR=$ps_stage_dir powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \
            'Compress-Archive -Path $env:STAGE_DIR -DestinationPath $env:ZIP_PATH -Force'
    else
        printf 'Could not find zip, python3, or powershell.exe for archive creation.\n' >&2
        exit 1
    fi
)

printf 'Created release zip: %s\n' "$zip_path"
printf 'Install: extract into <game>/BepInEx/plugins/\n'
