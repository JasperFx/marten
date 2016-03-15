using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Marten.Schema.Sequences
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
    }
}