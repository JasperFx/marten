using JasperFx;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Sessions;

internal static class TenantedSessionFactory
{
    // |---------------------------------------------------------|
    // | SCENARIOS                                               |
    // |---------------------------------------------------------|
    // | SESSION     | SLICE       | STORAGE   | RESULT          |
    // |-------------|-------------|-----------|-----------------|
    // | DEFAULT     | DEFAULT     | SINGLE    | THE SAME        |
    // | DEFAULT     | DEFAULT     | CONJOINED | THE SAME        |
    // | DEFAULT     | NON-DEFAULT | SINGLE    | THE SAME        |
    // | DEFAULT     | NON-DEFAULT | CONJOINED | NEW NON-DEFAULT |
    // | NON-DEFAULT | DEFAULT     | SINGLE    | NEW DEFAULT     |
    // | NON-DEFAULT | DEFAULT     | CONJOINED | THE SAME        |
    // | NON-DEFAULT | NON-DEFAULT | SINGLE    | NEW DEFAULT     |
    // | NON-DEFAULT | NON-DEFAULT | CONJOINED | THE SAME        |
    internal static DocumentSessionBase UseTenancyBasedOnSliceAndStorage(
        this DocumentSessionBase session,
        IDocumentStorage storage,
        IEventSlice slice
    )
    {
        var shouldApplyConjoinedTenancy =
            session.TenantId != slice.Tenant.TenantId
            && slice.Tenant.TenantId != StorageConstants.DefaultTenantId
            && storage.TenancyStyle == TenancyStyle.Conjoined
            && session.DocumentStore.Options.Tenancy.IsTenantStoredInCurrentDatabase(
                session.Database,
                slice.Tenant.TenantId
            );

        if (shouldApplyConjoinedTenancy)
            return session.WithTenant(slice.Tenant.TenantId);

        var isDefaultTenantAllowed =
            session.SessionOptions.AllowAnyTenant
            || session.Options.Advanced.DefaultTenantUsageEnabled;

        var shouldApplyDefaultTenancy =
            isDefaultTenantAllowed
            && session.TenantId != StorageConstants.DefaultTenantId
            && storage.TenancyStyle == TenancyStyle.Single;

        if (shouldApplyDefaultTenancy)
            return session.WithDefaultTenant();

        return session;
    }

    private static DocumentSessionBase WithTenant(this IDocumentSession session, string tenantId) =>
        (DocumentSessionBase)session.ForTenant(tenantId);

    private static DocumentSessionBase WithDefaultTenant(this IDocumentSession session) =>
        (DocumentSessionBase)session.ForTenant(StorageConstants.DefaultTenantId);
}
