namespace Marten.Schema.Identity.Sequences
{
    public class LongHiloGenerator : IIdGeneration<long>
    {
        public LongHiloGenerator(ISequence sequence)
        {
            Sequence = sequence;
        }

        public ISequence Sequence { get; }

        public long Assign(long existing, out bool assigned)
        {
            if (existing > 0)
            {
                assigned = false;
                return existing;
            }

            var next = Sequence.NextLong();

            assigned = true;

            return next;
        }
    }
}