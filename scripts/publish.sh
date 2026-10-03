#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd "$(dirname "$0")/.." && pwd)"
task_rid="${1:-linux-x64}"
case "$task_rid" in win-x64|osx-arm64|osx-x64|linux-x64|linux-arm64) ;; *) echo 'Unsupported runtime' >&2; exit 1 ;; esac
task_output="$task_root/artifacts/publish/$task_rid"
mkdir -p "$task_output"
dotnet publish "$task_root/src/TableLens.Desktop/TableLens.Desktop.csproj" -c Release -r "$task_rid" --self-contained true -o "$task_output" -p:PublishTrimmed=false
cp "$task_root/LICENSE" "$task_root/README.md" "$task_output/"
cp "$task_root/THIRD_PARTY_NOTICES.md" "$task_output/"
cp -R "$task_root/licenses" "$task_root/docs" "$task_output/"
printf '%s\n' "$task_output"
