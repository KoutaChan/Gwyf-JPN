#!/usr/bin/env sh
set -eu

game_dir='D:/SteamLibrary/steamapps/common/Gamble With Your Friends'
translation_file=''
build=0

usage() {
    printf '%s\n' 'Usage: sh scripts/install.sh [--game-dir DIR] [--translations FILE] [--build]'
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
        --translations|--translation-file|-t|-Translations|-TranslationFile)
            if [ "$#" -lt 2 ]; then usage >&2; exit 2; fi
            translation_file=$2
            shift 2
            ;;
        --translations=*|--translation-file=*)
            translation_file=${1#*=}
            shift
            ;;
        --build|-b|-Build)
            build=1
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

if [ "$build" -eq 1 ]; then
    sh "$script_dir/build.sh" --game-dir "$game_dir" --plugin
fi

plugin_source_dir=$repo_root/src/GwyfJpn.Plugin/bin/Release/netstandard2.1
plugin_dll=$plugin_source_dir/GwyfJpn.Plugin.dll
core_dll=$plugin_source_dir/GwyfJpn.Core.dll
plugin_target_dir=$game_dir/BepInEx/plugins/GwyfJpn

mkdir -p "$plugin_target_dir"
cp -f "$plugin_dll" "$plugin_target_dir/"
cp -f "$core_dll" "$plugin_target_dir/"

config_source_dir=$plugin_source_dir/config
if [ -d "$config_source_dir" ]; then
    config_target_dir=$plugin_target_dir/config
    mkdir -p "$config_target_dir"
    cp -R "$config_source_dir/." "$config_target_dir/"
fi

font_source_dir=$plugin_source_dir/fonts
if [ -d "$font_source_dir" ]; then
    font_target_dir=$plugin_target_dir/fonts
    mkdir -p "$font_target_dir"
    cp -R "$font_source_dir/." "$font_target_dir/"
fi

if [ -z "$translation_file" ]; then
    translation_file=$repo_root/translations/ja/translations.ja.json
fi

translation_target_dir=$plugin_target_dir/translations/ja
mkdir -p "$translation_target_dir"
cp -f "$translation_file" "$translation_target_dir/translations.ja.json"

printf 'Installed plugin to: %s\n' "$plugin_target_dir"
