#nullable enable
using System;

namespace Marten.Schema;

/// <summary>
///     Creates an index on the predefined Last Modified column
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class IndexedCreatedTimestampAttribute: MartenAttribute
{
    public override void Modify(DocumentMapping mapping)
    {
        mapping.AddCreatedTimestampIndex();
    }
}
