#nullable enable
using System;
using Marten.Storage;
using Weasel.Postgresql.Tables.Partitioning;

namespace Marten.Schema;

/// <summary>
/// Directs Marten to store this document type with no multi-tenancy. This may
/// be valuable to explicitly exempt individual document types when using multi-tenancy
/// policies
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class SingleTenantedAttribute: MartenAttribute
{
    public override void Modify(DocumentMapping mapping)
    {
        mapping.TenancyStyle = TenancyStyle.Conjoined;
        if (mapping.Partitioning != null && mapping.Partitioning is ListPartitioning lp &&
            lp.PartitionManager is ManagedListPartitions)
        {
            mapping.Partitioning = null;
        }
    }
}
