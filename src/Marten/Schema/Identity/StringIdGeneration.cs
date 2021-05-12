using System;
using System.Collections.Generic;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using Marten.Storage;
#nullable enable
namespace Marten.Schema.Identity
{
    /// <summary>
    /// Validating identity strategy for user supplied string identities
    /// </summary>
    public class StringIdGeneration: IIdGeneration
    {
        public IEnumerable<Type> KeyTypes { get; } = new[] { typeof(string) };

        public bool RequiresSequences { get; } = false;
        public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
        {
            var document = new Use(mapping.DocumentType);
            method.Frames.Code($"if (string.IsNullOrEmpty({{0}}.{mapping.IdMember.Name})) throw new InvalidOperationException(\"Id/id values cannot be null or empty\");", document);
            method.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
        }

    }
}
