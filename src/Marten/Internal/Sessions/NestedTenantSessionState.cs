#nullable enable
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Internal.Sessions;

internal static class NestedTenantSessionState
{
    /// <summary>
    ///     #4947 — a nested ForTenant() session keeps its own identity map / version tracker so that
    ///     conjoined documents (where the same id means a different document per tenant) cannot bleed
    ///     across tenants (#4801). Tenancy-neutral documents are the opposite case: there is exactly
    ///     one document per id for the whole database, so the parent session and every ForTenant()
    ///     view of it must see the very same tracked instance. Alias the parent's identity-map (and
    ///     version) entry for this document type into the nested session the first time the type is
    ///     used through the nested session — before any load/store can create a competing entry.
    /// </summary>
    internal static void ShareTenantNeutralStateWith<T>(this QuerySession nested, QuerySession parent,
        IDocumentStorage<T> storage) where T : notnull
    {
        // Same tenant as the parent: the whole map/tracker is already shared wholesale
        if (ReferenceEquals(nested.ItemMap, parent.ItemMap))
        {
            return;
        }

        // Only identity-mapped (identity map + dirty checking) storage tracks documents in the session
        if (storage is not ISharedTenantNeutralSessionState shared)
        {
            return;
        }

        // Conjoined documents are genuinely per-tenant — keep them isolated (#4801)
        if (shared.TenancyStyle == TenancyStyle.Conjoined)
        {
            return;
        }

        // Under database-per-tenant the same id in another tenant's database is a *different*
        // document even for a tenancy-neutral doc type, so only share within one database.
        if (!ReferenceEquals(nested.Database, parent.Database))
        {
            return;
        }

        shared.ShareTenantNeutralStateWith(parent, nested);
    }
}
