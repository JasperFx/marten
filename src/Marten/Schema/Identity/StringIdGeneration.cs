using System;
using System.Collections.Generic;
using Baseline;
using Marten.Storage;

namespace Marten.Schema.Identity
{
    public class StringIdGeneration: IIdGeneration, IIdGenerator<string>
    {
        public IEnumerable<Type> KeyTypes { get; } = new[] { typeof(string) };

        public IIdGenerator<T> Build<T>()
        {
            return this.As<IIdGenerator<T>>();
        }

        public bool RequiresSequences { get; } = false;

        public string Assign(ITenant tenant, string existing, out bool assigned)
        {
            if (existing.IsEmpty())
            {
                throw new InvalidOperationException("Id/id values cannot be null or empty");
            }

            assigned = false;

            return existing;
        }
    }
}
