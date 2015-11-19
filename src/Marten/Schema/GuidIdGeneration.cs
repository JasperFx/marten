using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Marten.Schema
{
    public class GuidIdGeneration : IIdGeneration
    {
        public IEnumerable<StorageArgument> ToArguments()
        {
            return Enumerable.Empty<StorageArgument>();
        }

        public string AssignmentBodyCode(MemberInfo idMember)
        {
            return
                $"if (document.{idMember.Name} == System.Guid.Empty) document.{idMember.Name} = System.Guid.NewGuid();";
        }
    }
}