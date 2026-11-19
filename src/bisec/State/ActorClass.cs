using System;

namespace BiSec.Library
{
    // Drive classes from APK com.isisic.remote.hoermann.global.ActorClasses (the transition's gk).
    public enum ActorClass
    {
        // A gk below UNDEFINED_OFFSET that the app does not name.
        Unknown = 0,
        VersamaticGelenkarmSingle = 0x011A,
        VersamaticGelenkarmDouble = 0x011B,
        VersamaticGleitschieneSingle = 0x011E,
        VersamaticGleitschieneDouble = 0x011F,
        Fes1 = 0x0401,
        Fes1_1 = 0x0402,
        SmartKey = 0x0604,
        Portamatic = 0x0701,
        Ese = 0x0902,
        HetS = 0x0905,
        HetE1 = 0x0908,
        HetE2 = 0x0909,
        HetE2Sl = 0x090D,
        // Any gk at or above UNDEFINED_OFFSET; such transitions carry no HCP block.
        Undefined = 0xFC00,
    }

    public static class ActorClasses
    {
        public const int UndefinedOffset = 0xFC00;

        public static ActorClass FromGk(int gk)
        {
            if (gk >= UndefinedOffset)
                return ActorClass.Undefined;

            if (Enum.IsDefined(typeof(ActorClass), gk))
                return (ActorClass)gk;

            return ActorClass.Unknown;
        }
    }
}
