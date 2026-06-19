#!/usr/bin/env sh
set -eu

translations=''

usage() {
    printf '%s\n' 'Usage: sh scripts/validate-translations.sh [--translations FILE]'
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --translations|-t|-Translations)
            if [ "$#" -lt 2 ]; then usage >&2; exit 2; fi
            translations=$2
            shift 2
            ;;
        --translations=*)
            translations=${1#*=}
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

if [ -z "$translations" ]; then
    translations=$repo_root/translations/ja/translations.ja.json
fi

sh "$script_dir/build.sh"

extractor_exe=$repo_root/src/GwyfJpn.Extractor/bin/Release/net6.0/GwyfJpn.Extractor.exe
extractor_dll=$repo_root/src/GwyfJpn.Extractor/bin/Release/net6.0/GwyfJpn.Extractor.dll

if [ -f "$extractor_exe" ]; then
    "$extractor_exe" validate --translations "$translations"
else
    dotnet "$extractor_dll" validate --translations "$translations"
fi
