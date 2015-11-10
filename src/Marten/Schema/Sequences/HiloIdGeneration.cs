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
            if (idMember.GetMemberType() == typeof (int))
            {
                return $"document.{idMember.Name} = _sequence.NextInt();";
            }
            else
            {
                return $"document.{idMember.Name} = _sequence.NextLong();";
            }

            
        }
    }
}