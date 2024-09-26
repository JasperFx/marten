#nullable enable
using System;

namespace Marten.Schema;

/// <summary>
/// Directs Marten to ignore any kind of table partitioning policy
/// for just this document type
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class DoNotPartitionAttribute: MartenAttribute
{
    public override void Modify(DocumentMapping mapping)
    {
        mapping.DisablePartitioningIfAny = true;
        mapping.Partitioning = null;
    }
}
