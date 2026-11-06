using Artemis.Core.Modules;

namespace Artemis.Plugins.Modules.LinuxLockKeys.DataModels;

public class LockKeysDataModel : DataModel
{
    [DataModelProperty(Name = "Caps Lock")]
    public bool CapsLock { get; set; }

    [DataModelProperty(Name = "Num Lock")]
    public bool NumLock { get; set; }

    [DataModelProperty(Name = "Scroll Lock")]
    public bool ScrollLock { get; set; }
}
