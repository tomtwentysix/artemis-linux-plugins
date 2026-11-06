using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace Artemis.Plugins.Modules.Performance.Services.Performance;

[SupportedOSPlatform("linux")]
public class LinuxPerformanceService : IPerformanceService
{
    private readonly object _lock = new();
    private ulong _lastIdle;
    private ulong _lastTotal;

    // CPU usage is the share of non-idle time since the previous call.
    // First line of /proc/stat: "cpu user nice system idle iowait irq softirq steal guest guest_nice"
    // guest/guest_nice are already counted in user/nice, so only the first 8 fields are summed.
    public float GetCpuUsage()
    {
        string line = File.ReadLines("/proc/stat").First();
        ulong[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1).Take(8).Select(ulong.Parse).ToArray();

        ulong idle = fields[3] + fields[4]; // idle + iowait
        ulong total = 0;
        foreach (ulong value in fields)
            total += value;

        lock (_lock)
        {
            bool firstSample = _lastTotal == 0;
            ulong deltaIdle = idle - _lastIdle;
            ulong deltaTotal = total - _lastTotal;
            _lastIdle = idle;
            _lastTotal = total;

            if (firstSample || deltaTotal == 0)
                return 0;
            return (float) (deltaTotal - deltaIdle) / deltaTotal * 100f;
        }
    }

    // MemAvailable is the kernel's estimate of memory usable without swapping,
    // which matches what Windows reports as "available".
    public long GetPhysicalAvailableMemoryInMiB() => ReadMemInfoKiB("MemAvailable:") / 1024;

    public long GetTotalMemoryInMiB() => ReadMemInfoKiB("MemTotal:") / 1024;

    private static long ReadMemInfoKiB(string key)
    {
        foreach (string line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith(key, StringComparison.Ordinal))
                return long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
        }

        return 0;
    }
}
