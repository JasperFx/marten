#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;

namespace Marten.Schema.Identity;

/// <summary>
///     Validating identity strategy for user supplied string identities
/// </summary>
public class StringIdGeneration: IIdGeneration
{
    public bool IsNumeric { get; } = false;

    public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
    {
        var document = new Use(mapping.DocumentType);
        method.Frames.Code(
            $"if (string.IsNullOrEmpty({{0}}.{mapping.IdMember.Name})) throw new InvalidOperationException(\"Id/id values cannot be null or empty\");",
            document);
        method.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
    }
}
