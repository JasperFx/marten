using System;

namespace Marten.Testing.Events.Daemon.TestingSupport
{
    public class Trip
    {
        public Guid Id { get; set; }


        public int EndedOn { get; set; }

        public double Traveled { get; set; }

        public string State { get; set; }

        public bool Active { get; set; }

        public int StartedOn { get; set; }

        protected bool Equals(Trip other)
        {
            return Id.Equals(other.Id) && EndedOn == other.EndedOn && Traveled.Equals(other.Traveled) && State == other.State && Active == other.Active && StartedOn == other.StartedOn;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Trip) obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, EndedOn, Traveled, State, Active, StartedOn);
        }

        public override string ToString()
        {
            return $"{nameof(Id)}: {Id}, {nameof(EndedOn)}: {EndedOn}, {nameof(Traveled)}: {Traveled}, {nameof(State)}: {State}, {nameof(Active)}: {Active}, {nameof(StartedOn)}: {StartedOn}";
        }
    }
}
