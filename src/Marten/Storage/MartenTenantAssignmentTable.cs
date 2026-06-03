using Weasel.Postgresql.Tables;

namespace Marten.Storage;

/// <summary>
/// #4607: Marten-side derivative of Weasel's <see cref="TenantAssignmentTable"/> that
/// adds a <c>disabled</c> boolean column so <see cref="ShardedTenancy"/> can support the
/// <see cref="JasperFx.MultiTenancy.IDynamicTenantSource{T}.DisableTenantAsync"/> /
/// <see cref="JasperFx.MultiTenancy.IDynamicTenantSource{T}.EnableTenantAsync"/> lifecycle
/// the same way <see cref="MasterTableTenancy"/> does (mirrors
/// <c>TenantTable.DisabledColumn</c>).
///
/// <para>
/// Subclassed on the Marten side rather than added to Weasel so existing Marten pools
/// pick up the new column via Weasel's additive table-delta migration (the <c>NOT NULL
/// DEFAULT false</c> means existing rows default to enabled) without requiring a coordinated
/// Weasel release. Marten owns the only construction site (<see cref="ShardedTenancy"/>'s
/// <c>PoolFeatureSchema</c>), so the substitution is fully contained.
/// </para>
/// </summary>
internal class MartenTenantAssignmentTable: TenantAssignmentTable
{
    /// <summary>The added column's name — used by <see cref="ShardedTenancy"/>'s SQL queries.</summary>
    public const string DisabledColumn = "disabled";

    public MartenTenantAssignmentTable(string schemaName): base(schemaName)
    {
        // NOT NULL with a `false` default means the column can be added to an existing
        // pool (legacy rows backfill to enabled) without a manual migration.
        AddColumn<bool>(DisabledColumn).NotNull().DefaultValueByExpression("false");
    }
}
