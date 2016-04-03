using System.Collections.Generic;
using System.Reflection;
using Marten.Util;

namespace Marten.Schema.Sequences
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
    }
}