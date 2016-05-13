using System;
using System.Collections.Generic;
using System.Reflection;

namespace Marten.Schema.Identity
{
    public interface IIdGeneration
    {
        IEnumerable<StorageArgument> ToArguments();
        string AssignmentBodyCode(MemberInfo idMember);

        IEnumerable<Type> KeyTypes { get; }

        IIdGeneration<T> Build<T>(IDocumentSchema schema);
    }


}