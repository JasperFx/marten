using System.Collections.Generic;
using System.Reflection;

namespace Marten.Schema
{
    public interface IIdGeneration
    {
        IEnumerable<StorageArgument> ToArguments();
        string AssignmentBodyCode(MemberInfo idMember);
    }
}