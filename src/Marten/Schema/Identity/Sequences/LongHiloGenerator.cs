using System;
using Marten.Storage;

namespace Marten.Schema.Identity.Sequences
{
    public class LongHiloGenerator : IIdGenerator<long>
    {
        private readonly Type _documentType;

        public LongHiloGenerator(Type documentType)
        {
            _documentType = documentType;
        }

        public long Assign(ITenant tenant, long existing, out bool assigned)
        {
            if (existing > 0)
            {
                assigned = false;
                return existing;
            }

            var next = tenant.Sequences.SequenceFor(_documentType).NextLong();

            assigned = true;

            return next;
        }
    }
}