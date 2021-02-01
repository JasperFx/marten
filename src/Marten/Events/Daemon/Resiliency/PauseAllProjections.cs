using System;
using System.Threading.Tasks;

namespace Marten.Events.Daemon.Resiliency
{
    internal class PauseAllProjections: IContinuation
    {
        public PauseAllProjections(TimeSpan delay)
        {
            Delay = delay;
        }

        public TimeSpan Delay { get; }

        protected bool Equals(PauseAllProjections other)
        {
            return Delay.Equals(other.Delay);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((PauseAllProjections) obj);
        }

        public override int GetHashCode()
        {
            return Delay.GetHashCode();
        }

        public override string ToString()
        {
            return $"{nameof(Delay)}: {Delay}";
        }
    }
}
