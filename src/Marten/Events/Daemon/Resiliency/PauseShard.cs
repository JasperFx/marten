using System;
using System.Threading.Tasks;

namespace Marten.Events.Daemon.Resiliency
{
    internal class PauseShard: IContinuation
    {
        public TimeSpan Delay { get; }

        public PauseShard(TimeSpan delay)
        {
            Delay = delay;
        }

        protected bool Equals(PauseShard other)
        {
            return Delay.Equals(other.Delay);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((PauseShard) obj);
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
