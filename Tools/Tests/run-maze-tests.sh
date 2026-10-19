#!/usr/bin/env bash
set -euo pipefail

test_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
maze_dotnet="${UNITY_DOTNET:-}"
if [[ -z "$maze_dotnet" ]]; then
    if command -v dotnet >/dev/null 2>&1 && [[ -n "$(dotnet --list-sdks)" ]]; then
        maze_dotnet="$(command -v dotnet)"
    else
        shopt -s nullglob
        candidates=( "$HOME"/Unity/Hub/Editor/*/Editor/Data/DotNetSdk/dotnet )
        if [[ ${#candidates[@]} -eq 0 ]]; then
            echo "No .NET SDK found. Set UNITY_DOTNET to Unity's DotNetSdk/dotnet." >&2
            exit 1
        fi
        maze_dotnet="${candidates[-1]}"
    fi
fi

maze_test_output="$(mktemp -d)"
trap 'rm -rf -- "$maze_test_output"' EXIT
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
"$maze_dotnet" build "$test_dir/Maze/MazeTests.csproj" --nologo --verbosity quiet \
    --output "$maze_test_output/bin" -p:BaseIntermediateOutputPath="$maze_test_output/obj/"
"$maze_dotnet" "$maze_test_output/bin/MazeTests.dll"
