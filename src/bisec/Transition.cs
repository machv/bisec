using System;
using System.Collections;

namespace BiSec.Library
{
    public class Transition
    {
        /// <summary>
        /// 100 is OPEN, 0 = CLOSED
        /// 200 = UNLOCKED, 0 = LOCKED????
        /// </summary>
        int _actualState;
        /// <summary>
        /// 100 is OPEN, 0 = CLOSED
        /// </summary>
        int _requestedState;
        bool _error;
        bool _autoClose;
        int _driveTime;
        int _gk;
        Hcp _hcp;
        byte[] _exst;
        DateTime _time;
        bool _ignoreRetries;

        public int ActualStateInPercent => _actualState / 2;

        public Hcp Hcp => _hcp;

        public bool IsDriving
        {
            get
            {
                return _driveTime != 0 || _hcp.Driving;
            }
        }

        public DriveDirection? DrivingDirection
        {
            get
            {
                if (_driveTime == 0 && _hcp.Driving)
                {
                    if (_hcp.DrivingToClose)
                    {
                        return DriveDirection.TO_CLOSE;
                    }
                    else
                    {
                        return DriveDirection.TO_OPEN;
                    }
                }
                else if (_driveTime > 0)
                {
                    if (_requestedState > _actualState)
                    {
                        return DriveDirection.TO_OPEN;
                    }
                    else
                    {
                        return DriveDirection.TO_CLOSE;
                    }
                }
                else
                {
                    return null;
                }
            }
        }

        public Transition(byte[] bytes)
        {
            //var byte3 = new BitArray(new byte[] { bytes[2] });
            //_error = byte3[7];
            //_autoClose = byte3[6];

            _actualState = bytes[0];
            _requestedState = bytes[1];

            if (bytes[2] >> 6 > 0)
            {
                if ((bytes[2] & 128) > 0) // bit 7
                    _error = true;

                if ((bytes[2] & 64) > 0) // bit 6
                    _autoClose = true;
            }

            if (!_error)
                _driveTime = ((bytes[2] & ~240) << 8) + bytes[3]; // clearing bit 6 and 7 of byte[2] + byte[3] to get total driveTime

            _gk = (bytes[4] << 8) + bytes[5]; // probably used with receivers to control 3rd party doors

            if(bytes[4] < 252)
                _hcp = new Hcp(bytes[6..8]);

            byte[] slice = bytes[8..16];
            Array.Reverse(slice);
            _exst = slice;
            _time = DateTime.Now;
            /*
             val byte3 = BitSet.valueOf(ba[2].toByteArray())
            return Transition(
                stateInPercent = ba[0].toUByte().toInt() / 2,
                desiredStateInPercent = ba[1].toUByte().toInt() / 2,
                error = byte3[7],
                autoClose = byte3[6],
                driveTime = ba[3].toInt(),  // TODO: clear 6th and 7th bit from byte3 and shift add it here
                gk = ByteBuffer.wrap(ba.copyOfRange(4, 6)).short.toInt(),
                hcp = HCP.from(ba.copyOfRange(6, 8)),
                exst = ba.copyOfRange(8, 16).toList().reversed(),
                time = LocalDateTime.now(),
                ignoreRetries = true
            )
             */


        }
    }
}
