using System;
using System.Collections.Generic;
using LamarCodeGeneration;

namespace Marten.Schema.Identity
{
    public interface IIdGeneration
    {
        IEnumerable<Type> KeyTypes { get; }

        bool RequiresSequences { get; }
        void GenerateCode(GeneratedMethod method, DocumentMapping mapping);
    }
}
