using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Marten.Schema.Identity
{
    public class NoOpIdGeneration : IIdGeneration
    {
        public IEnumerable<StorageArgument> ToArguments()
        {
            return Enumerable.Empty<StorageArgument>();
        }

        public string AssignmentBodyCode(MemberInfo idMember)
        {
            return "\r\nBLOCK:assigned = false;\r\nEND\r\n";
        }

        public IEnumerable<Type> KeyTypes { get; } = new Type[] { typeof(int), typeof(long), typeof(string), typeof(Guid) };


        public IIdGenerator<T> Build<T>(IDocumentSchema schema)
        {
            return new NoOpIdGenerator<T>();
        }

        public class NoOpIdGenerator<T> : IIdGenerator<T>
        {
            public T Assign(T existing, out bool assigned)
            {
                assigned = false;
                return existing;
            }
        }
    }
}