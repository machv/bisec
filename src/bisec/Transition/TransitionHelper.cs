using System;

namespace BiSec.Library
{
    // Port of APK refactor.bisecur._5_UTIL.HmTransitionHelper.
    public static class TransitionHelper
    {
        public static ActorState GetActorState(Transition transition)
        {
            double actual = transition == null ? 0 : transition.ActualState;

            return GetActorStateFromPercent(actual / 2);
        }

        public static ActorState GetActorStateFromPercent(double percent)
        {
            if (percent == 0)
                return ActorState.Closed;

            if (percent > 0 && percent < 33.5)
                return ActorState.Half3;

            if (percent >= 33.5 && percent < 66.5)
                return ActorState.Half2;

            if (percent >= 66.5 && percent < 100)
                return ActorState.Half1;

            if (percent == 100)
                return ActorState.Open;

            return ActorState.Closed;
        }

        public static bool HasError(Transition transition)
        {
            EnsureNotNull(transition);

            if (transition.Error)
                return true;

            return transition.Hcp != null && transition.Hcp.Error;
        }

        // The app's "hint" is the HCP not-referenced flag.
        public static bool HasHint(Transition transition)
        {
            EnsureNotNull(transition);

            return transition.Hcp != null && transition.Hcp.NotReferenced;
        }

        public static bool IsOpened(Transition transition)
        {
            EnsureNotNull(transition);

            if (transition.Hcp != null)
                return transition.Hcp.PositionOpen;

            return transition.ActualState == 200;
        }

        public static bool IsClosed(Transition transition)
        {
            EnsureNotNull(transition);

            if (transition.Hcp != null)
                return transition.Hcp.PositionClose;

            return transition.ActualState == 0;
        }

        public static bool IsDefinedHalfOpened(Transition transition)
        {
            EnsureNotNull(transition);

            if (transition.Gk == (int)ActorClass.Ese)
                return transition.ActualState == 100;

            return transition.Hcp != null && transition.Hcp.HalfOpen;
        }

        public static bool IsHalfOpened(Transition transition)
        {
            EnsureNotNull(transition);

            if (transition.Hcp != null)
                return transition.Hcp.HalfOpen;

            return transition.ActualState > 0 && transition.ActualState < 200;
        }

        public static bool IsDriving(Transition transition)
        {
            EnsureNotNull(transition);

            if (transition.Hcp != null)
                return transition.Hcp.Driving || transition.DriveTime != 0;

            return transition.DriveTime != 0;
        }

        public static bool IsDrivingToClose(Transition transition)
        {
            if (!IsDriving(transition))
                return false;

            if (transition.Hcp != null)
                return transition.Hcp.DrivingToClose;

            return transition.ActualState > transition.RequestedState && transition.DriveTime > 0;
        }

        public static bool IsDrivingToOpen(Transition transition)
        {
            if (!IsDriving(transition))
                return false;

            if (transition.Hcp != null)
                return !transition.Hcp.DrivingToClose;

            return transition.ActualState < transition.RequestedState && transition.DriveTime > 0;
        }

        private static void EnsureNotNull(Transition transition)
        {
            if (transition == null)
                throw new ArgumentNullException(nameof(transition));
        }
    }
}
