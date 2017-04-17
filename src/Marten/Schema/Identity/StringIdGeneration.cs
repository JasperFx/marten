using System;
using System.Collections.Generic;
using Baseline;
using Marten.Storage;

namespace Marten.Schema.Identity
{
    public class StringIdGeneration : IIdGeneration, IIdGenerator<string>
    {
        public IEnumerable<Type> KeyTypes { get; } = new[] {typeof(string)};


        public IIdGenerator<T> Build<T>(ITenant tenant)
        {
            return this.As<IIdGenerator<T>>();
        }

        public string Assign(string existing, out bool assigned)
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