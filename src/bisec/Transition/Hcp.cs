using System.Collections;

namespace BiSec.Library
{
    public class Hcp
    {
        bool _positionOpen;
        bool _positionClose;
        bool _optionRelais;
        bool _lightBarrier;
        bool _error;
        bool _drivingToClose;
        bool _driving;
        bool _halfOpened;
        bool _forecastLeadTime;
        bool _learned;
        bool _notReferenced;

        public bool PositionOpen => _positionOpen;
        public bool PositionClose => _positionClose;
        public bool OptionRelais => _optionRelais;
        public bool LightBarrier => _lightBarrier;
        public bool Error => _error;
        public bool DrivingToClose => _drivingToClose;
        public bool Driving => _driving;
        public bool HalfOpen => _halfOpened;
        public bool ForecastLeadTime => _forecastLeadTime;
        public bool Learned => _learned;
        public bool NotReferenced => _notReferenced;

        public Hcp(byte[] bytes)
        {
            BitArray ba = new BitArray(bytes);
            _positionOpen = ba.Get(0);
            _positionClose = ba.Get(1);
            _optionRelais = ba.Get(2);
            _lightBarrier = ba.Get(3);
            _error = ba.Get(4);
            _drivingToClose = ba.Get(5);
            _driving = ba.Get(6);
            _halfOpened = ba.Get(7);
            _forecastLeadTime = ba.Get(8);
            _learned = ba.Get(9);
            _notReferenced = ba.Get(10);
        }
    }
}
