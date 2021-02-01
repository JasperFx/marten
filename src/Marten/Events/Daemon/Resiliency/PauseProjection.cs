using System;
using System.Threading.Tasks;

namespace Marten.Events.Daemon.Resiliency
{
    internal class PauseProjection: IContinuation
    {
        public TimeSpan Delay { get; }

        public PauseProjection(TimeSpan delay)
        {
            Delay = delay;
        }

        protected bool Equals(PauseProjection other)
        {
            return Delay.Equals(other.Delay);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((PauseProjection) obj);
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
