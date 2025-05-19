#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Core.Reflection;

namespace Marten.Schema.Identity;

/// <summary>
///     Simple Guid identity generation
/// </summary>
public class GuidIdGeneration: IIdGeneration
{
    public bool IsNumeric { get; } = false;

    public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
    {
        var document = new Use(mapping.DocumentType);
        method.Frames.Code(
            $"if ({{0}}.{mapping.IdMember.Name} == Guid.Empty) _setter({{0}}, {typeof(Guid).FullNameInCode()}.NewGuid());",
            document);
        method.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
    }
}
