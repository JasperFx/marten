#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;

namespace Marten.Storage;

public class CompositeDocumentCleaner: IDocumentCleaner
{
    private readonly ITenancy _tenancy;

    public CompositeDocumentCleaner(ITenancy tenancy)
    {
        _tenancy = tenancy;
    }


    public void DeleteAllDocuments()
    {
        DeleteAllDocumentsAsync().GetAwaiter().GetResult();
    }

    public Task DeleteAllDocumentsAsync(CancellationToken ct = default)
    {
        return applyToAll(d => d.DeleteAllDocumentsAsync(ct));
    }

    public void DeleteDocumentsByType(Type documentType)
    {
        DeleteDocumentsByTypeAsync(documentType).GetAwaiter().GetResult();
    }

    public Task DeleteDocumentsByTypeAsync(Type documentType, CancellationToken ct = default)
    {
        return applyToAll(d => d.DeleteDocumentsByTypeAsync(documentType, ct));
    }

    public void DeleteDocumentsExcept(params Type[] documentTypes)
    {
        DeleteDocumentsExceptAsync(default, documentTypes).GetAwaiter().GetResult();
    }

    public Task DeleteDocumentsExceptAsync(CancellationToken ct, params Type[] documentTypes)
    {
        return applyToAll(d => d.DeleteDocumentsExceptAsync(ct, documentTypes));
    }

    public void CompletelyRemove(Type documentType)
    {
        CompletelyRemoveAsync(documentType).GetAwaiter().GetResult();
    }

    public Task CompletelyRemoveAsync(Type documentType, CancellationToken ct = default)
    {
        return applyToAll(d => d.CompletelyRemoveAsync(documentType, ct));
    }

    public void CompletelyRemoveAll()
    {
        CompletelyRemoveAllAsync().GetAwaiter().GetResult();
    }

    public Task CompletelyRemoveAllAsync(CancellationToken ct = default)
    {
        return applyToAll(d => d.CompletelyRemoveAllAsync(ct));
    }

    public void DeleteAllEventData()
    {
        DeleteAllEventDataAsync().GetAwaiter().GetResult();
    }

    public Task DeleteAllEventDataAsync(CancellationToken ct = default)
    {
        return applyToAll(d => d.DeleteAllEventDataAsync(ct));
    }

    public void DeleteSingleEventStream(Guid streamId, string? tenantId = null)
    {
        DeleteSingleEventStreamAsync(streamId, tenantId).GetAwaiter().GetResult();
    }

    public async Task DeleteSingleEventStreamAsync(Guid streamId, string? tenantId = null,
        CancellationToken ct = default)
    {
        if (tenantId.IsEmpty())
        {
            await applyToAll(d => d.DeleteSingleEventStreamAsync(streamId, tenantId, ct)).ConfigureAwait(false);
        }

        var tenant = await _tenancy.GetTenantAsync(tenantId).ConfigureAwait(false);
        await tenant.Database.DeleteSingleEventStreamAsync(streamId, tenantId, ct).ConfigureAwait(false);
    }

    public void DeleteSingleEventStream(string streamId, string? tenantId = null)
    {
        DeleteSingleEventStreamAsync(streamId, tenantId).GetAwaiter().GetResult();
    }

    public async Task DeleteSingleEventStreamAsync(string streamId, string? tenantId = null,
        CancellationToken ct = default)
    {
        if (tenantId.IsEmpty())
        {
            await applyToAll(d => d.DeleteSingleEventStreamAsync(streamId, tenantId, ct)).ConfigureAwait(false);
        }

        var tenant = await _tenancy.GetTenantAsync(tenantId).ConfigureAwait(false);
        await tenant.Database.DeleteSingleEventStreamAsync(streamId, tenantId, ct).ConfigureAwait(false);
    }

    private async Task applyToAll(Func<IMartenDatabase, Task> func)
    {
        var databases = await _tenancy.BuildDatabases().ConfigureAwait(false);
        foreach (IMartenDatabase database in databases) await func(database).ConfigureAwait(false);
    }
}
