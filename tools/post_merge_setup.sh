#!/usr/bin/env bash
# Non-interactive reconciliation for the native Unity source workspace.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

command -v python3 >/dev/null
command -v dotnet >/dev/null

echo "Checking the Unity compilation runner protocol (simulated processes, not Unity)."
python3 -m unittest discover -s tools/tests -p 'test_unity_compile.py'

echo "Checking native source and isolated runtime/editor API contracts."
dotnet run --project testingReplic/Tests/SourceChecks/SourceChecks.csproj --configuration Release

echo "Post-merge source setup passed. No Unity Editor compile, Play Mode or device check was run."
echo "Use tools/unity_compile.py with the matching licensed Unity Editor for full compilation."