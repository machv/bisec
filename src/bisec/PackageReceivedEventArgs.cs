using System;

namespace BiSec.Library
{
    public class PackageReceivedEventArgs : EventArgs
    {
        public Package Package { get; }

        /// <summary>
        /// True when the package did not complete a pending request: it was pushed by the
        /// gateway (tag 0xFF, WIFI_FOUND) or nothing was waiting for its tag.
        /// </summary>
        public bool IsUnsolicited { get; }

        public PackageReceivedEventArgs(Package package, bool isUnsolicited)
        {
            Package = package;
            IsUnsolicited = isUnsolicited;
        }
    }
}
