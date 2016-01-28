using System;

namespace Marten.Services
{
    public class RequestCounterThreshold
    {
        public static RequestCounterThreshold Empty { get { return new RequestCounterThreshold(0, () => { }); } }

        private readonly int _threshold;
        public bool HasThreshold => _threshold > 0;

        private readonly Action _exceedingThresholdAction;
        
        /// <summary>
        /// Create a new threshhold for the maximum number of requests
        /// </summary>
        /// <param name="threshold">The maximum number of requests</param>
        /// <param name="exceedingThresholdAction">An action to be called whenever the threshold is surpassed by a single session</param>
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