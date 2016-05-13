using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;

namespace Marten.Schema.Identity
{
    public class StringIdGeneration : IIdGeneration, IIdGenerator<string>
    {
        public IEnumerable<StorageArgument> ToArguments()
        {
            return Enumerable.Empty<StorageArgument>();
        }

        public string AssignmentBodyCode(MemberInfo idMember)
        {
            var message = $"String {idMember.Name} values cannot be null or empty";
            return $@"
if (string.IsNullOrEmpty(document.{idMember.Name})) throw new InvalidOperationException(`{message}`);
assigned = false;
";
        }

        public IEnumerable<Type> KeyTypes { get; } = new Type[] {typeof(string)};


        public IIdGenerator<T> Build<T>(IDocumentSchema schema)
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