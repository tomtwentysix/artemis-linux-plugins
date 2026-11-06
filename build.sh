#!/usr/bin/env bash
# Builds two Artemis plugin zips for Linux:
#   dist/Performance-Linux.zip   - the official Performance module plus a Linux backend (CPU + RAM usage)
#   dist/LinuxSensors.zip        - temperatures from /sys/class/hwmon (what lm-sensors reads)
# Then import each zip in Artemis: Settings > Plugins > Import plugin.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="$(mktemp -d)"
DIST="$HERE/dist"
trap 'rm -rf "$WORK"' EXIT

for tool in git zip dotnet; do
    command -v "$tool" >/dev/null || { echo "Missing '$tool'. Install it and re-run." >&2; exit 1; }
done
if ! dotnet --list-sdks | grep -q '^10\.'; then
    echo "The .NET 10 SDK is required (the plugins target net10.0)." >&2
    echo "Install it from your distro or https://dotnet.microsoft.com/download/dotnet/10.0" >&2
    exit 1
fi

echo "==> Fetching Artemis.Plugins source"
git clone --quiet --depth 1 https://github.com/Artemis-RGB/Artemis.Plugins "$WORK/repo"
MODULES="$WORK/repo/src/Modules"

echo "==> Adding the Linux backend to the Performance module"
PERF="$MODULES/Artemis.Plugins.Modules.Performance"
cp "$HERE/performance/Bootstrapper.cs" "$PERF/Bootstrapper.cs"
cp "$HERE/performance/plugin.json" "$PERF/plugin.json"
cp "$HERE/performance/LinuxPerformanceService.cs" "$PERF/Services/Performance/LinuxPerformanceService.cs"

echo "==> Adding the Linux Sensors module"
cp -r "$HERE/sensors" "$MODULES/Artemis.Plugins.Modules.LinuxSensors"

mkdir -p "$DIST"
package() {
    local project="$1" zipname="$2" out="$WORK/publish/$2"
    echo "==> Building $zipname"
    dotnet publish "$project" --configuration Release --output "$out" -nologo -v quiet
    rm -f "$DIST/$zipname.zip"
    (cd "$out" && zip -qr "$DIST/$zipname.zip" .)
}
package "$PERF/Artemis.Plugins.Modules.Performance.csproj" "Performance-Linux"
package "$MODULES/Artemis.Plugins.Modules.LinuxSensors/Artemis.Plugins.Modules.LinuxSensors.csproj" "LinuxSensors"

echo
echo "Done. Import these in Artemis (Settings > Plugins > Import plugin):"
ls -1 "$DIST"/*.zip
