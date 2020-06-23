using System;
using System.Collections.Generic;
using LamarCodeGeneration;

namespace Marten.Schema.Identity
{
    public interface IIdGeneration
    {
        IEnumerable<Type> KeyTypes { get; }

        [Obsolete("Goes away in v4")]
        IIdGenerator<T> Build<T>();

        bool RequiresSequences { get; }
        void GenerateCode(GeneratedMethod method, DocumentMapping mapping);
    }
}
