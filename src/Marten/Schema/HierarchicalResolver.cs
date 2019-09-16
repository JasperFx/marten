using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;
using Marten.Storage;
using Marten.Util;
using Npgsql;

namespace Marten.Schema
{
    public class HierarchicalDocumentStorage<T>: DocumentStorage<T> where T : class
    {
        private readonly DocumentMapping _hierarchy;

        public HierarchicalDocumentStorage(DocumentMapping hierarchy)
            : base(hierarchy)
        {
            _hierarchy = hierarchy;
        }

        public override T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            if (reader.IsDBNull(startingIndex))
                return null;

            var offset = 0;
            var id = reader[startingIndex + ++offset];
            var typeAlias = reader.GetFieldValue<string>(startingIndex + ++offset);

            var version = reader.GetFieldValue<Guid>(startingIndex + ++offset);
            var lastMod = reader.GetValue(startingIndex + ++offset).MapToDateTime();
            var dotNetType = reader.GetFieldValue<string>(startingIndex + ++offset);
            
            var deleted = false;
            DateTime? deletedAt = null;
            if (_hierarchy.DeleteStyle == DeleteStyle.SoftDelete)
            {
                deleted = reader.GetFieldValue<bool>(startingIndex + ++offset);
                if (!reader.IsDBNull(startingIndex + ++offset))
                {
                    deletedAt = reader.GetValue(startingIndex + offset).MapToDateTime();
                }
            }

            string tenantId = null;
            if (_hierarchy.TenancyStyle == TenancyStyle.Conjoined)
            {
                tenantId = reader.GetFieldValue<string>(startingIndex + ++offset);
            }

            var metadata = new DocumentMetadata(lastMod, version, dotNetType, typeAlias, deleted, deletedAt)
            {
                TenantId = tenantId
            };

            var json = reader.GetTextReader(startingIndex);
            return map.Get<T>(id, _hierarchy.TypeFor(typeAlias), json, version, t => MetadataProjector.ProjectTo(t, metadata));
        }

        public override async Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            if (await reader.IsDBNullAsync(startingIndex, token).ConfigureAwait(false))
                return null;

            var offset = 0;
            var id = await reader.GetFieldValueAsync<object>(startingIndex + ++offset, token).ConfigureAwait(false);

            var typeAlias = await reader.GetFieldValueAsync<string>(startingIndex + ++offset, token).ConfigureAwait(false);

            var version = await reader.GetFieldValueAsync<Guid>(startingIndex + ++offset, token).ConfigureAwait(false);
            var lastMod = (await reader.GetFieldValueAsync<object>(startingIndex + ++offset, token).ConfigureAwait(false)).MapToDateTime();
            var dotNetType = await reader.GetFieldValueAsync<string>(startingIndex + ++offset, token).ConfigureAwait(false);

            var deleted = false;
            DateTime? deletedAt = null;
            if (_hierarchy.DeleteStyle == DeleteStyle.SoftDelete)
            {
                deleted = await reader.GetFieldValueAsync<bool>(startingIndex + ++offset, token).ConfigureAwait(false);
                if (!await reader.IsDBNullAsync(startingIndex + ++offset, token).ConfigureAwait(false))
                {
                    deletedAt = (await reader.GetFieldValueAsync<object>(startingIndex + offset, token).ConfigureAwait(false)).MapToDateTime();
                }
            }

            string tenantId = null;
            if (_hierarchy.TenancyStyle == TenancyStyle.Conjoined)
            {
                tenantId = await reader.GetFieldValueAsync<string>(startingIndex + ++offset, token).ConfigureAwait(false);
            }

            var metadata = new DocumentMetadata(lastMod, version, dotNetType, typeAlias, deleted, deletedAt)
            {
                TenantId = tenantId
            };

            var json = await reader.As<NpgsqlDataReader>().GetTextReaderAsync(startingIndex).ConfigureAwait(false);

            return map.Get<T>(id, _hierarchy.TypeFor(typeAlias), json, version, t => MetadataProjector.ProjectTo(t, metadata));
        }

        public override T Fetch(object id, DbDataReader reader, IIdentityMap map)
        {
            if (!reader.Read())
                return null;

            return Resolve(0, reader, map);
        }

        public override async Task<T> FetchAsync(object id, DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            var found = await reader.ReadAsync(token).ConfigureAwait(false);

            if (!found)
                return null;

            return await ResolveAsync(0, reader, map, token).ConfigureAwait(false);
        }
    }
}
