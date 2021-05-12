using System;
using System.Collections.Generic;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
#nullable enable
namespace Marten.Schema.Identity
{
    /// <summary>
    /// Simple Guid identity generation
    /// </summary>
    public class GuidIdGeneration: IIdGeneration
    {
        public IEnumerable<Type> KeyTypes { get; } = new[] { typeof(Guid) };

        public bool RequiresSequences { get; } = false;
        public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
        {
            var document = new Use(mapping.DocumentType);
            method.Frames.Code($"if ({{0}}.{mapping.IdMember.Name} == Guid.Empty) _setter({{0}}, {typeof(Guid).FullNameInCode()}.NewGuid());", document);
            method.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
        }
    }
}
