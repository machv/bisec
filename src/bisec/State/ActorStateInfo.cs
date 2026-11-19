namespace BiSec.Library
{
    /// <summary>
    /// What the legacy app derives from a transition, as plain values. Properties that a
    /// drive class or group type does not provide are null.
    /// </summary>
    public class ActorStateInfo
    {
        /// <summary>The app's state helper that was applied.</summary>
        public StateHelperKind Helper { get; internal set; }

        /// <summary>False where the app draws its "unavailable" icon: no transition, or a SmartKey without HCP block.</summary>
        public bool IsAvailable { get; internal set; }

        /// <summary>Position bucket the app draws (first leaf for a VersaMatic double drive).</summary>
        public ActorState State { get; internal set; }

        /// <summary>
        /// Second leaf of a VersaMatic double drive. The app only draws it for
        /// <see cref="Models.GroupType.SwingGateDouble"/> groups.
        /// </summary>
        public ActorState? SecondLeafState { get; internal set; }

        /// <summary>False while the app hides the position image (error, not referenced, driving, unknown lock state).</summary>
        public bool ShowState { get; internal set; }

        /// <summary>Position in percent (actual / 2), set only where the app prints it.</summary>
        public double? PositionPercent { get; internal set; }

        /// <summary>True where the app prints its "(H)" marker for the defined half-open position.</summary>
        public bool IsDefinedHalfOpen { get; internal set; }

        public bool IsDriving { get; internal set; }
        public DriveDirection? DrivingDirection { get; internal set; }

        /// <summary>False for drive classes whose direction the app never animates.</summary>
        public bool ShowDrivingDirection { get; internal set; }

        public bool HasError { get; internal set; }
        public bool NotReferenced { get; internal set; }
        public bool AutoClose { get; internal set; }

        public LockState? Lock { get; internal set; }

        /// <summary>HET-E1 "permanent open" relay.</summary>
        public bool? PermanentUnlock { get; internal set; }

        /// <summary>PortaMatic stop input, set only in the stop configurations.</summary>
        public bool? StopActive { get; internal set; }

        public BatteryState? Battery { get; internal set; }
        public BollardState? Bollard { get; internal set; }

        /// <summary>Set for light, other and jack groups.</summary>
        public bool? IsOn { get; internal set; }

        /// <summary>The app's state label.</summary>
        public StatusMessage Message { get; internal set; }

        /// <summary>Message the app prints in its state value, next to <see cref="PositionPercent"/> when both are set.</summary>
        public StatusMessage ValueMessage { get; internal set; }
    }
}
