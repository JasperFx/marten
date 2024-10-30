#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;

namespace Marten.Storage;

public class CompositeDocumentCleaner: IDocumentCleaner
{
    private readonly ITenancy _tenancy;
    private readonly StoreOptions _options;

    public CompositeDocumentCleaner(ITenancy tenancy, StoreOptions options)
    {
        _tenancy = tenancy;
        _options = options;
    }

    public Task DeleteAllDocumentsAsync(CancellationToken ct = default)
    {
        return applyToAll(d => d.DeleteAllDocumentsAsync(ct));
    }

    public Task DeleteDocumentsByTypeAsync(Type documentType, CancellationToken ct = default)
    {
        return applyToAll(d => d.DeleteDocumentsByTypeAsync(documentType, ct));
    }

    public Task DeleteDocumentsExceptAsync(CancellationToken ct, params Type[] documentTypes)
    {
        return applyToAll(d => d.DeleteDocumentsExceptAsync(ct, documentTypes));
    }

    public Task CompletelyRemoveAsync(Type documentType, CancellationToken ct = default)
    {
        return applyToAll(d => d.CompletelyRemoveAsync(documentType, ct));
    }

    public Task CompletelyRemoveAllAsync(CancellationToken ct = default)
    {
        return applyToAll(d => d.CompletelyRemoveAllAsync(ct));
    }

    public Task DeleteAllEventDataAsync(CancellationToken ct = default)
    {
        return applyToAll(d => d.DeleteAllEventDataAsync(ct));
    }

    public async Task DeleteSingleEventStreamAsync(Guid streamId, string? tenantId = null,
        CancellationToken ct = default)
    {
        if (tenantId.IsEmpty())
        {
            await applyToAll(d => d.DeleteSingleEventStreamAsync(streamId, tenantId, ct)).ConfigureAwait(false);
        }

        tenantId = _options.MaybeCorrectTenantId(tenantId);

        var tenant = await _tenancy.GetTenantAsync(tenantId).ConfigureAwait(false);
        await tenant.Database.DeleteSingleEventStreamAsync(streamId, tenantId, ct).ConfigureAwait(false);
    }

    public async Task DeleteSingleEventStreamAsync(string streamId, string? tenantId = null,
        CancellationToken ct = default)
    {
        if (tenantId.IsEmpty())
        {
            await applyToAll(d => d.DeleteSingleEventStreamAsync(streamId, tenantId, ct)).ConfigureAwait(false);
        }

        tenantId = _options.MaybeCorrectTenantId(tenantId);

        var tenant = await _tenancy.GetTenantAsync(tenantId).ConfigureAwait(false);
        await tenant.Database.DeleteSingleEventStreamAsync(streamId, tenantId, ct).ConfigureAwait(false);
    }

    private async Task applyToAll(Func<IMartenDatabase, Task> func)
    {
        var databases = await _tenancy.BuildDatabases().ConfigureAwait(false);
        foreach (var database in databases.OfType<IMartenDatabase>()) await func(database).ConfigureAwait(false);
    }
}
