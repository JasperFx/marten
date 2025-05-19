#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Marten.Storage;

namespace Marten.Schema.Identity.Sequences;

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

    public bool IsNumeric { get; } = true;

    public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
    {
        var document = new Use(mapping.DocumentType);


        if (mapping.IdType == typeof(int))
        {
            method.Frames.Code(
                $"if ({{0}}.{mapping.IdMember.Name} <= 0) _setter({{0}}, {{1}}.Sequences.SequenceFor({{2}}).NextInt());",
                document, Use.Type<IMartenDatabase>(), mapping.DocumentType);
        }
        else
        {
            method.Frames.Code(
                $"if ({{0}}.{mapping.IdMember.Name} <= 0) _setter({{0}}, {{1}}.Sequences.SequenceFor({{2}}).NextLong());",
                document, Use.Type<IMartenDatabase>(), mapping.DocumentType);
        }

        method.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
    }
}
