using System;
using System.Collections.Generic;

namespace Marten.Schema.Identity
{
    public interface IIdGeneration
    {
        IEnumerable<Type> KeyTypes { get; }

        IIdGenerator<T> Build<T>();

        bool RequiresSequences { get; }
    }



}