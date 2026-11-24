#nullable enable
using System;

namespace Marten.Schema;

/// <summary>
///     Creates an index on the predefined mt_created_at column, and turns that opt-in column on
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class IndexedCreatedAtAttribute: MartenAttribute
{
    public override void Modify(DocumentMapping mapping)
    {
        mapping.AddCreatedAtIndex();
    }
}
