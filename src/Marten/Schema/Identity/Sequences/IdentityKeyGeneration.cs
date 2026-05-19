#nullable enable
using System;

namespace Marten.Schema.Identity.Sequences;

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

    public bool IsNumeric { get; } = true;

    public Type[] DependentFeatures()
    {
        return new[] { typeof(SequenceFactory) };
    }
}
