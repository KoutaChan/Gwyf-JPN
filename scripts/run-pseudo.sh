#!/usr/bin/env sh
set -eu

game_dir='D:/SteamLibrary/steamapps/common/Gamble With Your Friends'

usage() {
    printf '%s\n' 'Usage: sh scripts/run-pseudo.sh [--game-dir DIR]'
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
pseudo_file=$repo_root/translations/ja/pseudo.ja.json

sh "$script_dir/extract.sh" --game-dir "$game_dir"
sh "$script_dir/install.sh" --game-dir "$game_dir" --translation-file "$pseudo_file" --build
printf '%s\n' 'Pseudo localization installed. Launch the game from Steam to test it.'
