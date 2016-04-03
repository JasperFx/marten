using System;
using System.Collections.Generic;
using System.Reflection;
using Marten.Schema;

namespace Marten.Testing.Schema.Sequences
{
    internal class IdentityKeyGeneration : IIdGeneration
    {
        public string AssignmentBodyCode(MemberInfo idMember)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<StorageArgument> ToArguments()
        {
            throw new NotImplementedException();
        }
    }
}