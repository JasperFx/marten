using System;

namespace Marten.Events.Daemon.Resiliency;

internal class PauseShard: IContinuation
{
    public PauseShard(TimeSpan delay)
    {
        Delay = delay;
    }

    public TimeSpan Delay { get; }

    protected bool Equals(PauseShard other)
    {
        return Delay.Equals(other.Delay);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((PauseShard)obj);
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
