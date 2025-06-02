#nullable enable
using System;

namespace Marten.Schema;

/// <summary>
///     Creates an index on the tenantId column. 
/// </summary>
/// <remarks>
///     Only applicable when using multi-tenancy for this table
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public class IndexedTenantIdAttribute
    : MartenAttribute
{
    public override void Modify(DocumentMapping mapping)
    {
        mapping.AddTenantIdIndex();
    }
}
