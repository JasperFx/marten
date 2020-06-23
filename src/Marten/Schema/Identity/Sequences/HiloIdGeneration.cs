using System;
using System.Collections.Generic;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using Marten.Storage;

namespace Marten.Schema.Identity.Sequences
{
    public class HiloIdGeneration: IIdGeneration
    {
        private readonly HiloSettings _hiloSettings;

        public HiloIdGeneration(Type documentType, HiloSettings hiloSettings)
        {
            _hiloSettings = hiloSettings;
            DocumentType = documentType;
        }

        public Type DocumentType { get; }

        public int MaxLo => _hiloSettings.MaxLo;

        public IEnumerable<Type> KeyTypes { get; } = new[] { typeof(int), typeof(long) };

        public IIdGenerator<T> Build<T>()
        {
            if (typeof(T) == typeof(int))
            {
                return (IIdGenerator<T>)new IntHiloGenerator(DocumentType);
            }

            return (IIdGenerator<T>)new LongHiloGenerator(DocumentType);
        }

        public bool RequiresSequences { get; } = true;
        public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
        {
            var document = new Use(mapping.DocumentType);


            if (mapping.IdType == typeof(int))
            {
                method.Frames.Code($"if ({{0}}.{mapping.IdMember.Name} <= 0) {{0}}.Id = {{1}}.Sequences.SequenceFor({{2}}).NextInt();", document, Use.Type<Marten.V4Internals.ITenant>(), mapping.DocumentType);
            }
            else
            {
                method.Frames.Code($"if ({{0}}.{mapping.IdMember.Name} <= 0) {{0}}.Id = {{1}}.Sequences.SequenceFor({{2}}).NextLong();", document, Use.Type<Marten.V4Internals.ITenant>(), mapping.DocumentType);
            }

            method.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
        }
    }
}
