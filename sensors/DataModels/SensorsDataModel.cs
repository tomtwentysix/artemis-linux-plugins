using Artemis.Core.Modules;

namespace Artemis.Plugins.Modules.LinuxSensors.DataModels;

public class SensorsDataModel : DataModel
{
    [DataModelProperty(Name = "CPU temperature", Affix = "°C")]
    public float CpuTemperature { get; set; }

    [DataModelProperty(Name = "GPU temperature", Affix = "°C", Description = "AMD and Intel GPUs only; the NVIDIA driver doesn't expose hwmon")]
    public float GpuTemperature { get; set; }

    [DataModelProperty(Name = "Hottest sensor", Affix = "°C")]
    public float HottestTemperature { get; set; }
}

// One of these is added per hwmon chip (k10temp, coretemp, amdgpu, nvme, ...),
// holding every temperature that chip reports.
public class ChipDataModel : DataModel
{
}
