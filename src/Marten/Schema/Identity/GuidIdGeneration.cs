using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Marten.Schema.Identity
{
    public class GuidIdGeneration : IIdGeneration
    {
        public IEnumerable<StorageArgument> ToArguments()
        {
            return Enumerable.Empty<StorageArgument>();
        }

        public string AssignmentBodyCode(MemberInfo idMember)
        {
            return $@"
BLOCK:if (document.{idMember.Name} == System.Guid.Empty)
document.{idMember.Name} = System.Guid.NewGuid();
assigned = true;
END
BLOCK:else
assigned = false;
END
";

        }

        public IEnumerable<Type> KeyTypes { get; } = new Type[] {typeof(Guid)};

        public IIdGenerator<T> Build<T>(IDocumentSchema schema)
        {
            return (IIdGenerator<T>) new GuidIdGenerator(Guid.NewGuid);
        }
    }
}