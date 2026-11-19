using BiSec.Library.Models;

namespace BiSec.Library
{
    /// <summary>
    /// UI-free port of the legacy app's state helpers (refactor.bisecur._2_SAL.stateHelper.*)
    /// and of the code that combines them (StateHelper.getStateLabel / getStateValue and
    /// StateImageBase.transition). The app's "requestable" flag is taken as true: a group
    /// that has a transition has a port its state was requested from.
    /// </summary>
    public static class ActorStateInterpreter
    {
        private enum PortamaticInput
        {
            Unknown,
            BoltError,
            BoltLocked,
            BoltUnlocked,
            StopActive,
            StopInactive,
        }

        // APK StateHelperFactory.getStateHelper
        public static StateHelperKind GetStateHelper(Transition transition)
        {
            if (transition == null || transition.Gk >= ActorClasses.UndefinedOffset)
                return StateHelperKind.Default;

            switch ((ActorClass)transition.Gk)
            {
                case ActorClass.Ese:
                    return StateHelperKind.Ese;
                case ActorClass.HetE1:
                    return StateHelperKind.HetE1;
                case ActorClass.HetE2:
                    return StateHelperKind.HetE2;
                case ActorClass.HetS:
                    return StateHelperKind.HetS;
                case ActorClass.HetE2Sl:
                    return StateHelperKind.HetE2Sl;
                case ActorClass.Portamatic:
                    return StateHelperKind.Portamatic;
                case ActorClass.Fes1:
                case ActorClass.Fes1_1:
                    return StateHelperKind.Fes;
                case ActorClass.VersamaticGelenkarmDouble:
                case ActorClass.VersamaticGleitschieneDouble:
                    return StateHelperKind.VersamaticDouble;
                case ActorClass.SmartKey:
                    return StateHelperKind.Smartkey;
                default:
                    return StateHelperKind.Default;
            }
        }

        public static ActorStateInfo Interpret(Transition transition, GroupType groupType)
        {
            var info = new ActorStateInfo
            {
                Helper = GetStateHelper(transition),
                State = TransitionHelper.GetActorState(transition),
                ShowDrivingDirection = true,
            };

            if (transition == null)
            {
                info.Message = StatusMessage.NoData;
                return info;
            }

            Hcp hcp = transition.Hcp;
            bool lightLike = groupType == GroupType.Light || groupType == GroupType.Other || groupType == GroupType.Jack;

            info.IsAvailable = true;
            info.HasError = TransitionHelper.HasError(transition);
            info.NotReferenced = TransitionHelper.HasHint(transition);
            info.AutoClose = transition.AutoClose;
            info.IsDriving = TransitionHelper.IsDriving(transition);
            info.DrivingDirection = transition.DrivingDirection;

            if (lightLike)
                info.IsOn = transition.ActualState == 0;

            ReadDriveInputs(info, transition);

            // APK StateImageBase.transition: an error, hint, auto-close or driving overlay
            // wins, and only without one is the helper asked for its own icon and state.
            if (info.HasError || info.NotReferenced)
            {
                info.ShowState = false;
            }
            else if (info.AutoClose)
            {
                info.ShowState = true;
            }
            else if (info.IsDriving)
            {
                info.ShowState = false;
            }
            else
            {
                info.ShowState = ShowsState(info.Helper, transition) && info.Lock != LockState.Error;

                if (info.ShowState)
                {
                    ForceState(info, transition);

                    // HET_E2_StateHelper.forceState clears hcp on the transition itself, so the
                    // labels the app renders after the image no longer see the HCP block.
                    if (info.Helper == StateHelperKind.HetE2)
                        hcp = null;
                }
            }

            info.Message = GetStateLabel(info, transition) ?? GetDefaultStateLabel(transition, hcp, lightLike);
            ApplyStateValue(info, transition, lightLike);

            return info;
        }

        private static void ReadDriveInputs(ActorStateInfo info, Transition transition)
        {
            byte[] exst = transition.Exst;

            switch (info.Helper)
            {
                case StateHelperKind.HetE1:
                    // HET_E1_ExstParser: E1/E2 are bits 0/1 of exst[5], relay 2 is bit 1 of exst[7].
                    info.Lock = GetBoltState(exst);
                    info.PermanentUnlock = (exst[7] & 2) == 2;
                    break;

                case StateHelperKind.HetE2Sl:
                    info.ShowDrivingDirection = false;
                    info.Bollard = GetBollardState(exst);
                    break;

                case StateHelperKind.Portamatic:
                    PortamaticInput input = GetPortamaticInput(exst);

                    if (IsPortamaticStopMode(exst))
                        info.StopActive = input == PortamaticInput.StopActive;

                    if (IsPortamaticLockMode(exst) && TransitionHelper.IsClosed(transition))
                    {
                        if (input == PortamaticInput.BoltLocked)
                            info.Lock = LockState.Locked;
                        else if (input == PortamaticInput.BoltUnlocked)
                            info.Lock = LockState.Unlocked;
                        else if (input == PortamaticInput.BoltError)
                            info.Lock = LockState.Error;
                    }
                    break;

                case StateHelperKind.Smartkey:
                    info.ShowDrivingDirection = false;
                    info.IsAvailable = transition.Hcp != null;

                    if (transition.Hcp != null)
                    {
                        // Smartkey_StateHelper: SmartkeyStates in exst[7], SmartkeyBatteryStates in exst[5].
                        // Every battery value other than OK gets the app's battery-empty icon.
                        info.Lock = GetSmartkeyLockState(exst[7]);
                        info.Battery = exst[5] == 0 ? BatteryState.Ok : exst[5] == 1 ? BatteryState.Low : BatteryState.Empty;
                    }
                    break;
            }
        }

        private static bool ShowsState(StateHelperKind helper, Transition transition)
        {
            if (helper == StateHelperKind.Smartkey)
                return transition.Hcp != null && transition.Exst[7] != 0;

            return true;
        }

        private static void ForceState(ActorStateInfo info, Transition transition)
        {
            byte[] exst = transition.Exst;

            switch (info.Helper)
            {
                case StateHelperKind.HetE2:
                    // HET_E2_StateHelper ignores the HCP block: open, closed or "half_2".
                    if (transition.ActualState == 200)
                        info.State = ActorState.Open;
                    else if (transition.ActualState == 0)
                        info.State = ActorState.Closed;
                    else
                        info.State = ActorState.Half2;
                    break;

                case StateHelperKind.HetE2Sl:
                    // ActorState.RETRACTED = open, EXTENDED = closed, NOT_RETRACTED = half_2.
                    if (info.Bollard == BollardState.Retracted)
                        info.State = ActorState.Open;
                    else if (info.Bollard == BollardState.Extended)
                        info.State = ActorState.Closed;
                    else
                        info.State = ActorState.Half2;
                    break;

                case StateHelperKind.VersamaticDouble:
                    // VERSAMATIC_DOUBLE_StateHelper: one position per leaf in exst[5] and exst[4].
                    info.State = TransitionHelper.GetActorStateFromPercent(exst[5] / 2.0);
                    info.SecondLeafState = TransitionHelper.GetActorStateFromPercent(exst[4] / 2.0);
                    break;

                case StateHelperKind.Smartkey:
                    if (exst[7] == 1)
                        info.State = ActorState.Half3;
                    else if (exst[7] >= 2 && exst[7] <= 5)
                        info.State = ActorState.Closed;

                    if (exst[5] != 0)
                        info.State = ActorState.Closed;
                    break;
            }
        }

        // The helper's getStateLabel; null makes the app fall back to Default_StateHelper.
        private static StatusMessage? GetStateLabel(ActorStateInfo info, Transition transition)
        {
            byte[] exst = transition.Exst;
            int actual = transition.ActualState;

            switch (info.Helper)
            {
                case StateHelperKind.HetE1:
                    return info.PermanentUnlock == true ? StatusMessage.PermanentUnlocked : StatusMessage.PermanentLocked;

                case StateHelperKind.HetE2:
                    if (actual > 0 && actual < 200)
                        return StatusMessage.HalfMoving;
                    return null;

                case StateHelperKind.HetE2Sl:
                    switch (exst[5] & 3)
                    {
                        case 2:
                            return StatusMessage.Retracted;
                        case 1:
                            return StatusMessage.Extended;
                        case 0:
                            return StatusMessage.NotRetracted;
                        default:
                            return StatusMessage.Error;
                    }

                case StateHelperKind.Portamatic:
                    if (transition.AutoClose)
                    {
                        if (actual == 0)
                            return StatusMessage.Closed;
                        if (actual == 200)
                            return StatusMessage.Opened;
                        return StatusMessage.Half;
                    }

                    if (GetPortamaticInput(exst) == PortamaticInput.BoltError)
                        return StatusMessage.Error;
                    return null;

                case StateHelperKind.Smartkey:
                    if (transition.Hcp == null)
                        return StatusMessage.None;

                    switch (exst[7])
                    {
                        case 0:
                            return StatusMessage.NotReferenced;
                        case 1:
                            return StatusMessage.Opened;
                        case 2:
                            return StatusMessage.Unlocked;
                        case 3:
                        case 4:
                        case 5:
                            return StatusMessage.Locked;
                        default:
                            return null;
                    }

                default:
                    return null;
            }
        }

        // APK Default_StateHelper.getStateLabel
        private static StatusMessage GetDefaultStateLabel(Transition transition, Hcp hcp, bool lightLike)
        {
            int actual = transition.ActualState;

            if (transition.Error)
                return StatusMessage.Error;

            if (transition.AutoClose)
                return StatusMessage.Opened;

            if (transition.DriveTime != 0)
                return StatusMessage.None;

            if (hcp != null)
            {
                if (lightLike)
                    return actual == 0 ? StatusMessage.On : StatusMessage.Off;

                if (hcp.Error)
                    return StatusMessage.Error;

                if (hcp.NotReferenced)
                    return StatusMessage.NotReferenced;

                if (hcp.PositionOpen)
                    return StatusMessage.Opened;

                if (hcp.PositionClose)
                    return StatusMessage.Closed;

                if (hcp.HalfOpen)
                    return StatusMessage.Half;
            }

            if (actual != transition.RequestedState)
                return StatusMessage.None;

            if (actual == 200)
                return StatusMessage.Opened;

            if (actual == 0)
                return StatusMessage.Closed;

            return StatusMessage.Half;
        }

        // The helper's getStateValue, falling back to Default_StateHelper.getStateValue.
        private static void ApplyStateValue(ActorStateInfo info, Transition transition, bool lightLike)
        {
            byte[] exst = transition.Exst;
            int actual = transition.ActualState;

            switch (info.Helper)
            {
                case StateHelperKind.Ese:
                    info.IsDefinedHalfOpen = TransitionHelper.IsDefinedHalfOpened(transition);
                    return;

                case StateHelperKind.HetS:
                case StateHelperKind.HetE2:
                case StateHelperKind.HetE2Sl:
                    return;

                case StateHelperKind.HetE1:
                    if (actual == 0)
                        info.ValueMessage = StatusMessage.Locked;
                    else if (actual == 200)
                        info.ValueMessage = StatusMessage.Unlocked;
                    else
                        info.ValueMessage = StatusMessage.Error;
                    return;

                case StateHelperKind.Portamatic:
                    PortamaticInput input = GetPortamaticInput(exst);

                    if (IsPortamaticStopMode(exst) && input == PortamaticInput.StopActive)
                    {
                        info.ValueMessage = StatusMessage.StopActive;

                        if (!TransitionHelper.IsClosed(transition) && !TransitionHelper.IsOpened(transition))
                            info.PositionPercent = actual / 2.0;
                        return;
                    }

                    if (IsPortamaticLockMode(exst) && TransitionHelper.IsClosed(transition))
                    {
                        if (input == PortamaticInput.BoltLocked)
                        {
                            info.ValueMessage = StatusMessage.Locked;
                            return;
                        }

                        if (input == PortamaticInput.BoltUnlocked)
                        {
                            info.ValueMessage = StatusMessage.Unlocked;
                            return;
                        }

                        if (input == PortamaticInput.BoltError)
                            return;
                    }
                    break;

                case StateHelperKind.Smartkey:
                    if (transition.Hcp == null || exst[7] <= 5)
                        return;
                    break;
            }

            if (info.IsDriving || lightLike)
                return;

            if (info.HasError || info.NotReferenced || TransitionHelper.IsOpened(transition) || TransitionHelper.IsClosed(transition))
                return;

            info.PositionPercent = actual / 2.0;
            info.IsDefinedHalfOpen = TransitionHelper.IsDefinedHalfOpened(transition);
        }

        // E1 only: locked, E2 only: unlocked, anything else: error
        // (HET_E1_StateHelper.getAdditionalStateImage, Portamatic_StateHelper.getLockState_Changer).
        private static LockState GetBoltState(byte[] exst)
        {
            bool e1 = (exst[5] & 1) == 1;
            bool e2 = (exst[5] & 2) == 2;

            if (e1 && !e2)
                return LockState.Locked;

            if (!e1 && e2)
                return LockState.Unlocked;

            return LockState.Error;
        }

        // PinParser: pin 1 is bit 1 and pin 3 is bit 0 of exst[5]; a pin is high when its bit is clear.
        private static BollardState GetBollardState(byte[] exst)
        {
            switch (exst[5] & 3)
            {
                case 2:
                    return BollardState.Retracted;
                case 0:
                    return BollardState.NotRetracted;
                default:
                    return BollardState.Extended;
            }
        }

        // PortamaticExstParser: configuration mode in exst[6] (PortamaticConfiguration).
        private static bool IsPortamaticLockMode(byte[] exst)
        {
            return exst[6] >= 1 && exst[6] <= 3;
        }

        private static bool IsPortamaticStopMode(byte[] exst)
        {
            return exst[6] >= 4 && exst[6] <= 5;
        }

        // Portamatic_StateHelper.getInputState
        private static PortamaticInput GetPortamaticInput(byte[] exst)
        {
            bool e1 = (exst[5] & 1) == 1;
            bool e2 = (exst[5] & 2) == 2;

            switch (exst[6])
            {
                case 1:
                    switch (GetBoltState(exst))
                    {
                        case LockState.Locked:
                            return PortamaticInput.BoltLocked;
                        case LockState.Unlocked:
                            return PortamaticInput.BoltUnlocked;
                        default:
                            return PortamaticInput.BoltError;
                    }
                case 2:
                    return e1 ? PortamaticInput.BoltLocked : PortamaticInput.BoltUnlocked;
                case 3:
                    return e2 ? PortamaticInput.BoltUnlocked : PortamaticInput.BoltLocked;
                case 4:
                    return e1 ? PortamaticInput.StopActive : PortamaticInput.StopInactive;
                case 5:
                    return e2 ? PortamaticInput.StopInactive : PortamaticInput.StopActive;
                default:
                    return PortamaticInput.Unknown;
            }
        }

        private static LockState? GetSmartkeyLockState(byte state)
        {
            switch (state)
            {
                case 0:
                    return LockState.Error;
                case 1:
                case 2:
                    return LockState.Unlocked;
                case 3:
                case 4:
                case 5:
                    return LockState.Locked;
                default:
                    return null;
            }
        }
    }
}
