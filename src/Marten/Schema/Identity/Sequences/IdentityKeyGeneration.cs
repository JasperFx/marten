using System;
using System.Collections.Generic;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using Marten.Storage;

namespace Marten.Schema.Identity.Sequences
{
    public class IdentityKeyGeneration: IIdGeneration
    {
        private readonly HiloSettings _hiloSettings;
        private readonly DocumentMapping _mapping;

        public IdentityKeyGeneration(DocumentMapping mapping, HiloSettings hiloSettings)
        {
            _mapping = mapping;
            _hiloSettings = hiloSettings ?? new HiloSettings();
        }

        public int MaxLo => _hiloSettings.MaxLo;

        public IEnumerable<Type> KeyTypes { get; } = new[] { typeof(string) };

        public bool RequiresSequences { get; } = true;
        public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
        {
            var document = new Use(mapping.DocumentType);

            method.Frames.Code($"if (string.{nameof(string.IsNullOrEmpty)}({{0}}.{mapping.IdMember.Name})) _setter({{0}}, \"{_mapping.Alias}\" + \"/\" + {{1}}.Sequences.SequenceFor({{2}}).NextLong());", document, Use.Type<ITenant>(), mapping.DocumentType);
            method.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
        }

        public Type[] DependentFeatures()
        {
            return new Type[] { typeof(SequenceFactory) };
        }
    }

}
