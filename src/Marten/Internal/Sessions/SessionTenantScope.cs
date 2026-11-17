#nullable enable
using Marten.Linq.Parsing.Methods;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Storage.Metadata;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Storage;

namespace Marten.Internal.Sessions;

/// <summary>
///     Which tenants a query session reads for conjoined documents and events when the query does not say
///     itself. Only <see cref="AdvancedOperations.QueryAcrossAllDatabasesAsync{T}(System.Func{IQuerySession,System.Threading.CancellationToken,System.Threading.Tasks.Task{System.Collections.Generic.IReadOnlyList{T}}},int,System.Threading.CancellationToken)" />
///     sets one: every tenant, or a fixed list of tenants. Without a scope a session reads its own tenant.
///     An <c>AnyTenant()</c> or <c>TenantIsOneOf()</c> in the query still wins.
/// </summary>
internal sealed class SessionTenantScope
{
    public static readonly SessionTenantScope AllTenants = new(null);

    private SessionTenantScope(string[]? tenantIds)
    {
        TenantIds = tenantIds;
    }

    public static SessionTenantScope For(string[] tenantIds) => new(tenantIds);

    /// <summary>
    ///     Null for every tenant
    /// </summary>
    public string[]? TenantIds { get; }

    /// <summary>
    ///     The LINQ filter that stands in for the session's own tenant filter. Both are
    ///     <see cref="ITenantFilter" />s, so the storage adds no tenant filter of its own.
    /// </summary>
    public ISqlFragment BuildFilter() => TenantIds == null ? new AnyTenant() : new TenantIsOneOfFilter(TenantIds);

    /// <summary>
    ///     For hand-written SQL that would otherwise append <c>and d.tenant_id = ?</c>
    /// </summary>
    public void AppendFilter(ICommandBuilder sql)
    {
        if (TenantIds == null) return;

        sql.Append(" and d.");
        sql.Append(TenantIdColumn.Name);
        sql.Append(" = ANY(");
        sql.AppendParameter(TenantIds, NpgsqlDbType.Array | NpgsqlDbType.Varchar);
        sql.Append(")");
    }

    public static SessionTenantScope? For(IStorageSession session) => (session as QuerySession)?.TenantScope;
}
