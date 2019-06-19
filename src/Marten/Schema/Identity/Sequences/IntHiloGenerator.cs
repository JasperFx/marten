using System;
using Marten.Storage;

namespace Marten.Schema.Identity.Sequences
{
    public class IntHiloGenerator: IIdGenerator<int>
    {
        private readonly Type _documentType;

        public IntHiloGenerator(Type documentType)
        {
            _documentType = documentType;
        }

        public int Assign(ITenant tenant, int existing, out bool assigned)
        {
            if (existing > 0)
            {
                assigned = false;
                return existing;
            }

            var next = tenant.Sequences.SequenceFor(_documentType).NextInt();

            assigned = true;

            return next;
        }
    }
}
