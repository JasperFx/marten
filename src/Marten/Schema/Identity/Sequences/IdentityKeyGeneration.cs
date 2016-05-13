using System;
using System.Collections.Generic;
using System.Reflection;
using Baseline;
using Marten.Util;

namespace Marten.Schema.Identity.Sequences
{
    public class IdentityKeyGeneration : StorageArgument, IIdGeneration
    {
        private readonly IDocumentMapping _mapping;
        private readonly HiloSettings _hiloSettings;

        public IdentityKeyGeneration(IDocumentMapping mapping, HiloSettings hiloSettings) : base("sequence", typeof(ISequence))
        {
            _mapping = mapping;
            _hiloSettings = hiloSettings;
        }

        public int Increment => _hiloSettings.Increment;
        public int MaxLo => _hiloSettings.MaxLo;

        public override object GetValue(IDocumentSchema schema)
        {
            return schema.Sequences.Hilo(_mapping.DocumentType, _hiloSettings);
        }

        public IEnumerable<StorageArgument> ToArguments()
        {
            return new StorageArgument[] { this };
        }

        public string AssignmentBodyCode(MemberInfo idMember)
        {
            var member = idMember.GetMemberType() == typeof (int) ? "NextInt" : "NextLong";
            return
                $@"
BLOCK:if (document.{idMember.Name} == null) 
document.{idMember.Name} = ""{_mapping.Alias}/"" +_sequence.{member}();
assigned = true;
END
BLOCK:else
assigned = false;
END
";
        }

        public IEnumerable<Type> KeyTypes { get; } = new Type[] {typeof(string)};

        public IIdGeneration<T> Build<T>(IDocumentSchema schema)
        {
            var sequence = schema.Sequences.Hilo(_mapping.DocumentType, _hiloSettings);
            return (IIdGeneration<T>) new IdentityKeyGenerator(_mapping.Alias, sequence);

        }
    }

    public class IdentityKeyGenerator : IIdGeneration<string>
    {
        public string Alias { get; set; }
        public ISequence Sequence { get; }

        public IdentityKeyGenerator(string alias, ISequence sequence)
        {
            Alias = alias;
            Sequence = sequence;
        }

        public string Assign(string existing, out bool assigned)
        {
            if (existing.IsEmpty())
            {
                var next = Sequence.NextLong();
                assigned = true;

                return $"{Alias}/{next}";
            }

            assigned = false;
            return existing;
        }
    }
}