namespace Marten.Services.Events
{
    public sealed class AggregationLookupStrategy
    {
        public readonly short Value;

        private bool Equals(AggregationLookupStrategy other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is AggregationLookupStrategy && Equals((AggregationLookupStrategy) obj);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public static readonly AggregationLookupStrategy UsePublicApply = new AggregationLookupStrategy(0);
        public static readonly AggregationLookupStrategy UsePrivateApply = new AggregationLookupStrategy(1);

        public static implicit operator short(AggregationLookupStrategy item)
        {
            return item.Value;
        }

        private AggregationLookupStrategy(short value)
        {
            Value = value;
        }
    }
}