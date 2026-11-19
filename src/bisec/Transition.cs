using BiSec.Library.Exceptions;
using System;

namespace BiSec.Library
{
    public class Transition
    {
        private const int ExstLength = 8;

        int _actualState;
        int _requestedState;
        bool _error;
        bool _autoClose;
        int _driveTime;
        int _gk;
        Hcp _hcp;
        byte[] _exst;
        DateTime _time;
        bool _ignoreRetries;

        public int ActualState => _actualState;
        public int RequestedState => _requestedState;
        public int ActualStateInPercent => _actualState / 2;
        public int DriveTime => _driveTime;
        public int Gk => _gk;
        public byte[] Exst => _exst;
        public bool Error => _error;
        public bool AutoClose => _autoClose;
        public bool IgnoreRetries => _ignoreRetries;
        public Hcp Hcp => _hcp;
        public DateTime Time => _time;
        public ActorClass ActorClass => ActorClasses.FromGk(_gk);

        public bool IsDriving => TransitionHelper.IsDriving(this);

        public DriveDirection? DrivingDirection
        {
            get
            {
                if (TransitionHelper.IsDrivingToClose(this))
                    return DriveDirection.TO_CLOSE;

                if (TransitionHelper.IsDrivingToOpen(this))
                    return DriveDirection.TO_OPEN;

                return null;
            }
        }

        public Transition(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            if (bytes.Length < 14)
                throw new InvalidPackageLengthException("Transition payload is too short.");

            int offset = 0;
            _actualState = bytes[offset++];
            _requestedState = bytes[offset++];

            int driveHighAndFlags = bytes[offset++];
            int driveLow = bytes[offset++];

            if (driveHighAndFlags >> 6 > 0)
            {
                if ((driveHighAndFlags & 128) > 0)
                    _error = true;

                if ((driveHighAndFlags & 64) > 0)
                    _autoClose = true;

                // APK Parsing.parseHmTransition clears the top nibble only when bit 6 or 7 is set.
                driveHighAndFlags &= ~240;
            }

            if (!_error)
                _driveTime = (driveHighAndFlags << 8) + driveLow;

            int gkHigh = bytes[offset++];
            int gkLow = bytes[offset++];
            _gk = (gkHigh << 8) + gkLow;

            if (gkHigh < 252)
            {
                EnsurePayloadLength(bytes, offset + 2);
                _hcp = new Hcp(bytes[offset..(offset + 2)]);
                offset += 2;
            }

            EnsurePayloadLength(bytes, offset + ExstLength);
            byte[] slice = bytes[offset..(offset + ExstLength)];
            Array.Reverse(slice);
            _exst = slice;
            _time = DateTime.Now;

            ApplyLegacyPostProcessing();
        }

        private static void EnsurePayloadLength(byte[] bytes, int requiredLength)
        {
            if (bytes.Length < requiredLength)
                throw new InvalidPackageLengthException("Transition payload is too short.");
        }

        private void ApplyLegacyPostProcessing()
        {
            if (_hcp != null)
            {
                if (!_hcp.Driving && (_driveTime > 0 || _hcp.ForecastLeadTime))
                {
                    _driveTime = 2;
                    _ignoreRetries = true;
                    _hcp = null;
                }
                else if (_hcp.Driving && _driveTime <= 0)
                {
                    _driveTime = 2;
                    _ignoreRetries = true;
                }
            }

            // APK ActorClasses.ESE = 0x0902 (2306). When the actor is an ESE drive
            // and the transition is otherwise considered driving, the legacy app
            // overrides driveTime to 5 and forces ignoreRetries=true.
            if (_gk == (int)ActorClass.Ese && IsDriving)
            {
                _driveTime = 5;
                _ignoreRetries = true;
            }

            // APK Parsing.parseHmTransition ends with IStateHelper.forceTransition, which
            // hands the transition back unchanged for every state helper.
        }
    }
}
