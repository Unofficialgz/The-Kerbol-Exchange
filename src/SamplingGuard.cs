using System;

namespace OreMarket
{
    // Exclusion/deduplication only; stock production callbacks and physical
    // balances authorize credit. A converter interval is NOT a physics tick.
    public sealed class SamplingGuard
    {
        private double previous = double.NaN;
        public bool Duplicate { get; private set; }
        public void Reset() { previous = double.NaN; Duplicate = false; }
        public bool Accept(double processedTime, double now)
        {
            Duplicate = false;
            const double tolerance = 1e-5; // Timestamp rounding only, never a production allowance.
            if (!TrackingState.ValidAmount(processedTime) || !TrackingState.ValidAmount(now) || processedTime > now + tolerance)
            { Reset(); return false; }
            double old = previous;
            if (!TrackingState.ValidAmount(old))
            {
                // Do not anchor in a backlog from before observation began.
                if (Math.Abs(now - processedTime) <= tolerance) previous = processedTime;
                return false;
            }
            if (processedTime < old) { Reset(); return false; }
            if (processedTime == old) { Duplicate = true; return false; }
            previous = processedTime;
            return true; // Continuously witnessed stock output may span ANY legitimate warp interval.
        }
    }
}

