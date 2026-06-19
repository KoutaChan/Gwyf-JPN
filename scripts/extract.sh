#!/usr/bin/env sh
set -eu

game_dir='D:/SteamLibrary/steamapps/common/Gamble With Your Friends'

usage() {
    printf '%s\n' 'Usage: sh scripts/extract.sh [--game-dir DIR]'
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
out_dir=$repo_root/translations/pipeline
asset_out=$out_dir/assets.raw.json
dll_out=$out_dir/dll.ldstr.json
merged_out=$out_dir/merged.candidates.json
pseudo_out=$repo_root/translations/ja/pseudo.ja.json
export_out=$out_dir/translation.export.jsonl

mkdir -p "$out_dir"
mkdir -p "$(dirname -- "$pseudo_out")"

sh "$script_dir/build.sh" --game-dir "$game_dir"

extractor_exe=$repo_root/src/GwyfJpn.Extractor/bin/Release/net6.0/GwyfJpn.Extractor.exe
extractor_dll=$repo_root/src/GwyfJpn.Extractor/bin/Release/net6.0/GwyfJpn.Extractor.dll

if [ -f "$extractor_exe" ]; then
    "$extractor_exe" extract --game-dir "$game_dir" --out-dir "$out_dir" --pseudo-out "$pseudo_out" --export-out "$export_out"
else
    dotnet "$extractor_dll" extract --game-dir "$game_dir" --out-dir "$out_dir" --pseudo-out "$pseudo_out" --export-out "$export_out"
fi

printf '%s\n' 'Extraction complete.'
printf '  Assets: %s\n' "$asset_out"
printf '  DLL:    %s\n' "$dll_out"
printf '  Merged: %s\n' "$merged_out"
printf '  Pseudo: %s\n' "$pseudo_out"
printf '  Export: %s\n' "$export_out"
