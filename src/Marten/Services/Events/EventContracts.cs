namespace Marten.Services.Events
{
    public sealed class EventContracts
    {
        public readonly string Value;

        private bool Equals(EventContracts other)
        {
            return string.Equals(Value, other.Value);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            return obj is EventContracts && Equals((EventContracts)obj);
        }

        public override int GetHashCode()
        {
            return Value?.GetHashCode() ?? 0;
        }

        public static implicit operator string(EventContracts item)
        {
            return item.Value;
        }

        private EventContracts(string value)
        {
            Value = value;
        }
    }
}
