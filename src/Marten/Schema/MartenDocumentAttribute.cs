#nullable enable
using System;

namespace Marten.Schema;

/// <summary>
/// Just marks a type as being a persisted Marten document for the AutoRegister()
/// feature
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class MartenDocumentAttribute: MartenAttribute
{
    public override void Register(Type discoveredType, StoreOptions options)
    {
        options.RegisterDocumentType(discoveredType);
    }
}
