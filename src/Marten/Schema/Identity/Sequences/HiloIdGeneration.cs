#nullable enable
using System;

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
}
