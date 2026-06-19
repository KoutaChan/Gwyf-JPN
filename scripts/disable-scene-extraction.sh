#!/usr/bin/env sh
set -eu

game_dir='D:/SteamLibrary/steamapps/common/Gamble With Your Friends'

usage() {
    printf '%s\n' 'Usage: sh scripts/disable-scene-extraction.sh [--game-dir DIR]'
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

flag=$game_dir/BepInEx/config/GwyfJpn/extract_all_scenes.flag
rm -f "$flag"

printf '%s\n' 'Disabled automated display extraction.'
