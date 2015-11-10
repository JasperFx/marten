using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Marten.Schema
{
    public class StringIdGeneration : IIdGeneration
    {
        public IEnumerable<StorageArgument> ToArguments()
        {
            return Enumerable.Empty<StorageArgument>();
        }

        public string AssignmentBodyCode(MemberInfo idMember)
        {
            var message = $"String {idMember.Name} values cannot be null or empty";
            return $"if (document.{idMember}.IsEmpty()) throw new InvalidOperationException(\"{message}\");";
        }
    }
}