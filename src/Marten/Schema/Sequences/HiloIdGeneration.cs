using System;
using System.Collections.Generic;
using System.Reflection;
using Marten.Util;

namespace Marten.Schema.Sequences
{
    public class HiloIdGeneration : StorageArgument, IIdGeneration
    {
        public Type DocumentType { get; private set; }

        public HiloIdGeneration(Type documentType) : base("sequence", typeof(ISequence))
        {
            DocumentType = documentType;
        }

        public HiloDef Parameters { get; } = new HiloDef();

        public override object GetValue(IDocumentSchema schema)
        {
            return schema.Sequences.HiLo(DocumentType, Parameters);
        }

        public IEnumerable<StorageArgument> ToArguments()
        {
            return new StorageArgument[] {this};
        }

        public string AssignmentBodyCode(MemberInfo idMember)
        {
            var member = idMember.GetMemberType() == typeof (int) ? "NextInt" : "NextLong";

            return $"if (document.{idMember.Name} == 0) document.{idMember.Name} = _sequence.{member}();" +
                   $"return document.{idMember.Name};";
        }
    }
}