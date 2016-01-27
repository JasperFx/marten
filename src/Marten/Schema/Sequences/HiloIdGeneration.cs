using System;
using System.Collections.Generic;
using System.Reflection;
using Marten.Util;

namespace Marten.Schema.Sequences
{
    public class HiloIdGeneration : StorageArgument, IIdGeneration
    {
        private readonly HiloDef _hiloDef;
        public Type DocumentType { get; private set; }

        public HiloIdGeneration(Type documentType, HiloDef hiloDef) : base("sequence", typeof(ISequence))
        {
            _hiloDef = hiloDef;
            DocumentType = documentType;
        }

        public int Increment => _hiloDef.Increment;
        public int MaxLo => _hiloDef.MaxLo;

        public override object GetValue(IDocumentSchema schema)
        {
            return schema.Sequences.HiLo(DocumentType, _hiloDef);
        }

        public IEnumerable<StorageArgument> ToArguments()
        {
            return new StorageArgument[] {this};
        }

        public string AssignmentBodyCode(MemberInfo idMember)
        {
            var member = idMember.GetMemberType() == typeof (int) ? "NextInt" : "NextLong";

            return $"if (document.{idMember.Name} == 0) document.{idMember.Name} = _sequence.{member}();";
        }
    }
}