#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;

namespace Marten.Schema.Identity;

/// <summary>
///     User-assigned identity strategy
/// </summary>
public class NoOpIdGeneration: IIdGeneration
{
    public bool IsNumeric => false;

    public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
    {
        var document = new Use(mapping.DocumentType);
        method.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
    }
}
