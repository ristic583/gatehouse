#!/usr/bin/env bash
# Line coverage baseline (M11-30, improvements.md §A1). Builds once, runs unit + integration (no AppHost) + contract tests
# with coverlet and merges them with ReportGenerator into artifacts/coverage/ (HTML + Summary.txt). Docker is needed for the
# integration tests. Web coverage: `cd src/web && npm run test:coverage`.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
out="$root/artifacts/coverage"
raw="$out/raw"
rm -rf "$raw"
mkdir -p "$raw"

dotnet tool restore >/dev/null
dotnet build Gatehouse.slnx -nologo -v q -clp:ErrorsOnly

run() {
  local project="$1"
  shift
  dotnet test "tests/$project" --no-build --nologo -v q --logger "console;verbosity=minimal" \
    --collect "XPlat Code Coverage" --results-directory "$raw/$project" "$@" \
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura \
    DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include="[Gatehouse.*]*" \
    DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude="[Gatehouse.*Tests]*" \
    DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.ExcludeByFile="**/Migrations/*.cs" | tail -n 5
}

status=0
run Gatehouse.UnitTests || status=1
run Gatehouse.IntegrationTests --filter "Category!=AppHost" || status=1
run Gatehouse.ContractTests || status=1

dotnet reportgenerator -reports:"$raw/**/coverage.cobertura.xml" -targetdir:"$out" \
  -reporttypes:"Html;TextSummary" -verbosity:Warning
echo "Coverage summary: $out/Summary.txt"
head -n 20 "$out/Summary.txt"
exit "$status"
