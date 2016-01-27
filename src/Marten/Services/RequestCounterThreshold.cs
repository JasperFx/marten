using System;

namespace Marten.Services
{
    public class RequestCounterThreshold
    {
        public static RequestCounterThreshold Empty { get { return new RequestCounterThreshold(0, () => { }); } }

        private readonly int _threshold;
        public bool HasThreshold { get { return _threshold > 0; } }

        private readonly Action _exceedingThresholdAction;
        
        public RequestCounterThreshold(int threshold, Action exceedingThresholdAction)
        {
            _threshold = threshold;
            _exceedingThresholdAction = exceedingThresholdAction;
        }

        public void ValidateCounter(int currentCount)
        {
            if (!HasThreshold) return;

            if (currentCount > _threshold)
            {
                _exceedingThresholdAction();
            }
        }
    }
}