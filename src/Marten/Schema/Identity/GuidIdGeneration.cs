using System;
using System.Collections.Generic;
using Marten.Storage;

namespace Marten.Schema.Identity
{
    public class GuidIdGeneration : IIdGeneration
    {
        public IEnumerable<Type> KeyTypes { get; } = new[] {typeof(Guid)};

        public IIdGenerator<T> Build<T>(ITenant tenant)
        {
            return (IIdGenerator<T>) new GuidIdGenerator(Guid.NewGuid);
        }
    }
}