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
            return $"if (string.IsNullOrEmpty(document.{idMember.Name})) throw new InvalidOperationException(\"{message}\");" +
                $"return document.{idMember.Name};";
        }
    }
}