namespace Marten.Schema.Identity.Sequences
{
    public class IntHiloGenerator : IIdGenerator<int>
    {
        public IntHiloGenerator(ISequence sequence)
        {
            Sequence = sequence;
        }

        public ISequence Sequence { get; }

        public int Assign(int existing, out bool assigned)
        {
            if (existing > 0)
            {
                assigned = false;
                return existing;
            }

            var next = Sequence.NextInt();

            assigned = true;

            return next;
        }
    }
}