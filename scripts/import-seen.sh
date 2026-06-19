#!/usr/bin/env sh
set -eu

seen='D:/SteamLibrary/steamapps/common/Gamble With Your Friends\BepInEx\config\GwyfJpn\display_seen.jsonl'
out=''

usage() {
    printf '%s\n' 'Usage: sh scripts/import-seen.sh [--seen FILE] [--out FILE]'
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --seen|-s|-Seen)
            if [ "$#" -lt 2 ]; then usage >&2; exit 2; fi
            seen=$2
            shift 2
            ;;
        --seen=*)
            seen=${1#*=}
            shift
            ;;
        --out|-o|-Out)
            if [ "$#" -lt 2 ]; then usage >&2; exit 2; fi
            out=$2
            shift 2
            ;;
        --out=*)
            out=${1#*=}
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

if [ -z "$out" ]; then
    out=$repo_root/translations/pipeline/runtime.seen.candidates.json
fi

sh "$script_dir/build.sh"

extractor_exe=$repo_root/src/GwyfJpn.Extractor/bin/Release/net6.0/GwyfJpn.Extractor.exe
extractor_dll=$repo_root/src/GwyfJpn.Extractor/bin/Release/net6.0/GwyfJpn.Extractor.dll

if [ -f "$extractor_exe" ]; then
    "$extractor_exe" import-seen --seen "$seen" --out "$out"
else
    dotnet "$extractor_dll" import-seen --seen "$seen" --out "$out"
fi

printf 'Imported display-sink candidates: %s\n' "$out"
