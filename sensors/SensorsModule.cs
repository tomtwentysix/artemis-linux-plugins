using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Artemis.Core;
using Artemis.Core.Modules;
using Artemis.Plugins.Modules.LinuxSensors.DataModels;

namespace Artemis.Plugins.Modules.LinuxSensors;

// Reads /sys/class/hwmon directly. This is the same kernel interface lm-sensors reads,
// so no external program, script or web API call is needed.
[PluginFeature(Name = "Linux Sensors", AlwaysEnabled = true)]
public class SensorsModule : Module<SensorsDataModel>
{
    private const string HwmonRoot = "/sys/class/hwmon";

    // Preferred (chip, label) pairs for the headline CPU/GPU values; a null label matches any sensor on that chip.
    private static readonly (string Chip, string Label)[] CpuCandidates =
    {
        ("k10temp", "Tdie"), ("k10temp", "Tctl"), ("zenpower", "Tdie"), ("coretemp", "Package id 0"),
        ("k10temp", null), ("zenpower", null), ("coretemp", null), ("cpu_thermal", null)
    };

    private static readonly (string Chip, string Label)[] GpuCandidates =
    {
        ("amdgpu", "edge"), ("amdgpu", null), ("nouveau", null), ("i915", null), ("xe", null)
    };

    private readonly List<Sensor> _sensors = new();
    private Sensor _cpu;
    private Sensor _gpu;

    public override List<IModuleActivationRequirement> ActivationRequirements => null;

    public override void Enable()
    {
        DiscoverSensors();
        AddTimedUpdate(TimeSpan.FromSeconds(1), _ => UpdateSensors(), "UpdateSensors");
    }

    public override void Disable()
    {
        _sensors.Clear();
        _cpu = null;
        _gpu = null;
        DataModel.ClearDynamicChildren();
    }

    public override void Update(double deltaTime)
    {
    }

    private void DiscoverSensors()
    {
        if (!Directory.Exists(HwmonRoot))
            return;

        // Seed with the static property names so dynamic keys never collide with them
        HashSet<string> chipKeys = new(typeof(SensorsDataModel).GetProperties().Select(p => p.Name));

        // hwmonN entries are symlinks, so enumerate entries rather than relying on directory detection
        foreach (string hwmon in Directory.EnumerateFileSystemEntries(HwmonRoot).OrderBy(NumericSuffix))
        {
            if (!Directory.Exists(hwmon))
                continue;

            string[] inputs;
            try
            {
                inputs = Directory.GetFiles(hwmon, "temp*_input");
            }
            catch (Exception)
            {
                continue;
            }

            if (inputs.Length == 0)
                continue;

            string chip = ReadText(Path.Combine(hwmon, "name")) ?? Path.GetFileName(hwmon);
            string chipKey = UniqueKey(Sanitize(chip), chipKeys);
            ChipDataModel chipModel = DataModel.AddDynamicChild(chipKey, new ChipDataModel(), chip).Value;

            HashSet<string> sensorKeys = new();
            foreach (string input in inputs.OrderBy(NumericSuffix))
            {
                string prefix = input.Substring(0, input.Length - "_input".Length); // e.g. .../temp1
                string label = ReadText(prefix + "_label") ?? Path.GetFileName(prefix);
                string key = UniqueKey(Sanitize(label), sensorKeys);

                DynamicChild<float> child = chipModel.AddDynamicChild(key, ReadTemp(input) ?? 0f,
                    new DataModelPropertyAttribute {Name = label, Affix = "°C"});
                _sensors.Add(new Sensor(chip, label, input, child));
            }
        }

        _cpu = Pick(CpuCandidates);
        _gpu = Pick(GpuCandidates);
    }

    private void UpdateSensors()
    {
        float hottest = 0;
        foreach (Sensor sensor in _sensors)
        {
            float? temperature = ReadTemp(sensor.FilePath);
            if (temperature == null)
                continue;

            sensor.Child.Value = temperature.Value;
            // Ignore obviously bogus readings (some chips report -273 or 255 for absent probes)
            if (temperature.Value > hottest && temperature.Value < 150)
                hottest = temperature.Value;
        }

        DataModel.CpuTemperature = _cpu?.Child.Value ?? 0;
        DataModel.GpuTemperature = _gpu?.Child.Value ?? 0;
        DataModel.HottestTemperature = hottest;
    }

    private Sensor Pick((string Chip, string Label)[] candidates)
    {
        foreach ((string chip, string label) in candidates)
        {
            Sensor match = _sensors.FirstOrDefault(s => s.Chip == chip && (label == null || s.Label == label));
            if (match != null)
                return match;
        }

        return null;
    }

    private static float? ReadTemp(string path)
    {
        // hwmon reports millidegrees Celsius; reads can fail (EIO/ENODATA) while a device sleeps
        string text = ReadText(path);
        if (text != null && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long milli))
            return milli / 1000f;
        return null;
    }

    private static string ReadText(string path)
    {
        try
        {
            string text = File.ReadAllText(path).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Sanitize(string value)
    {
        StringBuilder builder = new();
        foreach (char c in value)
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        return builder.Length == 0 ? "sensor" : builder.ToString();
    }

    private static string UniqueKey(string key, HashSet<string> used)
    {
        string candidate = key;
        for (int i = 2; !used.Add(candidate); i++)
            candidate = $"{key}_{i}";
        return candidate;
    }

    // Sort hwmon2 before hwmon10 and temp2 before temp10
    private static int NumericSuffix(string path)
    {
        string name = Path.GetFileName(path);
        string digits = new(name.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out int n) ? n : int.MaxValue;
    }

    private sealed record Sensor(string Chip, string Label, string FilePath, DynamicChild<float> Child);
}
