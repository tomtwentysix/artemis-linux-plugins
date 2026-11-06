# Artemis Linux plugins

Two [Artemis RGB](https://artemis-rgb.com/) data model plugins for Linux.

| Plugin | Data model |
| --- | --- |
| **Performance** | The official Performance module with a Linux backend: CPU usage from `/proc/stat`, RAM from `/proc/meminfo`. Same GUID as upstream, so profiles that use it work unchanged. |
| **Linux Sensors** | Temperatures from `/sys/class/hwmon` (the sensors lm-sensors reads): `CpuTemperature`, `GpuTemperature`, `HottestTemperature`. |

## Install

Download `Performance-Linux.zip` and `LinuxSensors.zip` from the [latest release](https://github.com/tomtwentysix/artemis-linux-plugins/releases/latest) and import each in Artemis under **Settings → Plugins → Import plugin**.

## Build

Requires git, zip and the .NET 10 SDK.

```bash
./build.sh
```

The script clones [Artemis.Plugins](https://github.com/Artemis-RGB/Artemis.Plugins), adds the Linux backend to the Performance module and the Linux Sensors project, and writes both zips to `dist/`.

## Licence

[PolyForm Noncommercial 1.0.0](LICENSE), matching upstream Artemis. The Performance module is derived from Artemis.Plugins by Artemis-RGB contributors.
