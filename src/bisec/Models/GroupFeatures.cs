using System;

namespace BiSec.Library.Models
{
    // APK HmGroup.FEATURE_ADD_PORT / FEATURE_INHERIT_PORT
    [Flags]
    public enum GroupFeatures
    {
        None = 0,
        AddPort = 1,
        InheritPort = 2,
    }
}
