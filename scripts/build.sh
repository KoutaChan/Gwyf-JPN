#!/usr/bin/env sh
set -eu

game_dir='D:/SteamLibrary/steamapps/common/Gamble With Your Friends'
plugin=0

usage() {
    printf '%s\n' 'Usage: sh scripts/build.sh [--game-dir DIR] [--plugin]'
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
        --plugin|-p|-Plugin)
            plugin=1
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
core_project=$repo_root/src/GwyfJpn.Core/GwyfJpn.Core.csproj
extractor_project=$repo_root/src/GwyfJpn.Extractor/GwyfJpn.Extractor.csproj
plugin_project=$repo_root/src/GwyfJpn.Plugin/GwyfJpn.Plugin.csproj

dotnet build "$core_project" -c Release
dotnet build "$extractor_project" -c Release

printf 'Built Extractor: %s\n' "$repo_root/src/GwyfJpn.Extractor/bin/Release/net6.0"

if [ "$plugin" -eq 1 ]; then
    dotnet build "$plugin_project" -c Release -p:GameDir="$game_dir"

    plugin_out_dir=$repo_root/src/GwyfJpn.Plugin/bin/Release/netstandard2.1
    core_dll=$repo_root/src/GwyfJpn.Core/bin/Release/netstandard2.1/GwyfJpn.Core.dll
    cp -f "$core_dll" "$plugin_out_dir/"
    printf 'Built Plugin: %s\n' "$plugin_out_dir"
fi
