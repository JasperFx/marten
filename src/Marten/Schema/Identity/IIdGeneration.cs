using System;
using System.Collections.Generic;
using Marten.Storage;

namespace Marten.Schema.Identity
{
    public interface IIdGeneration
    {
        IEnumerable<Type> KeyTypes { get; }

        IIdGenerator<T> Build<T>(ITenant tenant);
    }


}