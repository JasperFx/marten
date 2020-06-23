using System;
using System.Collections.Generic;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using Marten.Storage;

namespace Marten.Schema.Identity
{
    public class NoOpIdGeneration: IIdGeneration
    {
        public IEnumerable<Type> KeyTypes { get; } = new[] { typeof(int), typeof(long), typeof(string), typeof(Guid) };

        public IIdGenerator<T> Build<T>()
        {
            return new NoOpIdGenerator<T>();
        }

        public bool RequiresSequences { get; } = false;
        public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
        {
            var document = new Use(mapping.DocumentType);
            method.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
        }

        public class NoOpIdGenerator<T>: IIdGenerator<T>
        {
            public T Assign(ITenant tenant, T existing, out bool assigned)
            {
                assigned = false;
                return existing;
            }
        }
    }
}
