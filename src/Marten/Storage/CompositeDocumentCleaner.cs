using System;
using System.Threading.Tasks;
using Marten.Schema;
#nullable enable
namespace Marten.Storage
{
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

        private async Task applyToAll(Func<IMartenDatabase, Task> func)
        {
            var databases = await _tenancy.BuildDatabases().ConfigureAwait(false);
            foreach (IMartenDatabase database in databases)
            {
                await func(database).ConfigureAwait(false);
            }
        }

        public Task DeleteAllDocumentsAsync()
        {
            return applyToAll(d => d.DeleteAllDocumentsAsync());
        }

        public void DeleteDocumentsByType(Type documentType)
        {
            DeleteDocumentsByTypeAsync(documentType).GetAwaiter().GetResult();
        }

        public Task DeleteDocumentsByTypeAsync(Type documentType)
        {
            return applyToAll(d => d.DeleteDocumentsByTypeAsync(documentType));
        }

        public void DeleteDocumentsExcept(params Type[] documentTypes)
        {
            DeleteDocumentsExceptAsync(documentTypes).GetAwaiter().GetResult();
        }

        public Task DeleteDocumentsExceptAsync(params Type[] documentTypes)
        {
            return applyToAll(d => d.DeleteDocumentsExceptAsync(documentTypes));
        }

        public void CompletelyRemove(Type documentType)
        {
            CompletelyRemoveAsync(documentType).GetAwaiter().GetResult();
        }

        public Task CompletelyRemoveAsync(Type documentType)
        {
            return applyToAll(d => d.CompletelyRemoveAsync(documentType));
        }

        public void CompletelyRemoveAll()
        {
            CompletelyRemoveAllAsync().GetAwaiter().GetResult();
        }

        public Task CompletelyRemoveAllAsync()
        {
            return applyToAll(d => d.CompletelyRemoveAllAsync());
        }

        public void DeleteAllEventData()
        {
            DeleteAllEventDataAsync().GetAwaiter().GetResult();
        }

        public Task DeleteAllEventDataAsync()
        {
            return applyToAll(d => d.DeleteAllEventDataAsync());
        }

        public void DeleteSingleEventStream(Guid streamId, string? tenantId = null)
        {
            DeleteSingleEventStreamAsync(streamId, tenantId).GetAwaiter().GetResult();
        }

        public async Task DeleteSingleEventStreamAsync(Guid streamId, string? tenantId = null)
        {
            if (tenantId.IsEmpty())
            {
                await applyToAll(d => d.DeleteSingleEventStreamAsync(streamId, tenantId)).ConfigureAwait(false);
            }

            var tenant = await _tenancy.GetTenantAsync(tenantId).ConfigureAwait(false);
            await tenant.Database.DeleteSingleEventStreamAsync(streamId, tenantId).ConfigureAwait(false);
        }

        public void DeleteSingleEventStream(string streamId, string? tenantId = null)
        {
            DeleteSingleEventStreamAsync(streamId, tenantId).GetAwaiter().GetResult();
        }

        public async Task DeleteSingleEventStreamAsync(string streamId, string? tenantId = null)
        {
            if (tenantId.IsEmpty())
            {
                await applyToAll(d => d.DeleteSingleEventStreamAsync(streamId, tenantId)).ConfigureAwait(false);
            }

            var tenant = await _tenancy.GetTenantAsync(tenantId).ConfigureAwait(false);
            await tenant.Database.DeleteSingleEventStreamAsync(streamId, tenantId).ConfigureAwait(false);
        }
    }
}
