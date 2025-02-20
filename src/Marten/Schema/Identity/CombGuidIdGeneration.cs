#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Core;
using JasperFx.Core.Reflection;

namespace Marten.Schema.Identity;

/// <summary>
///     Comb Guid Id Generation. More info http://www.informit.com/articles/article.aspx?p=25862
/// </summary>
public class SequentialGuidIdGeneration: IIdGeneration
{
    public bool IsNumeric { get; } = false;

    public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
    {
        var document = new Use(mapping.DocumentType);
        method.Frames.Code(
            $"if ({{0}}.{mapping.IdMember.Name} == Guid.Empty) _setter({{0}}, {typeof(CombGuidIdGeneration).FullNameInCode()}.NewGuid());",
            document);
        method.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
    }
}
