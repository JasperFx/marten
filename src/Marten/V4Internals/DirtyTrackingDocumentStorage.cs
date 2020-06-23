using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Npgsql;

namespace Marten.V4Internals
{
    // TODO -- this isn't really implemented.
    // this is a copy of IdentityMap just to work for now
    public abstract class DirtyTrackingDocumentStorage<T, TId>: DocumentStorage<T, TId>
    {
        public DirtyTrackingDocumentStorage(DocumentMapping document): base(document)
        {
        }

                public sealed override void Eject(IMartenSession session, T document)
        {
            var id = Identity(document);
            if (session.ItemMap.TryGetValue(typeof(T), out var items))
            {
                if (items is Dictionary<TId, T> d)
                {
                    d.Remove(id);
                }
            }
        }

        public sealed override void Store(IMartenSession session, T document)
        {
            var id = AssignIdentity(document, session.Tenant);
            if (session.ItemMap.TryGetValue(typeof(T), out var items))
            {
                if (items is Dictionary<TId, T> d)
                {
                    d[id] = document;
                }
                else
                {
                    throw new InvalidOperationException($"Invalid id of type {typeof(TId)} for document type {typeof(T)}");
                }
            }
            else
            {
                var dict = new Dictionary<TId, T> {{id, document}};
                session.ItemMap.Add(typeof(T), dict);
            }
        }

        public sealed override void Store(IMartenSession session, T document, Guid? version)
        {
            var id = AssignIdentity(document, session.Tenant);
            if (session.ItemMap.TryGetValue(typeof(T), out var items))
            {
                if (items is Dictionary<TId, T> d)
                {
                    d[id] = document;
                }
                else
                {
                    throw new InvalidOperationException($"Invalid id of type {typeof(TId)} for document type {typeof(T)}");
                }
            }
            else
            {
                var dict = new Dictionary<TId, T> {{id, document}};
                session.ItemMap.Add(typeof(T), dict);
            }

            if (version != null)
            {
                session.Versions.StoreVersion<T, TId>(id, version.Value);
            }
            else
            {
                session.Versions.ClearVersion<T, TId>(id);
            }
        }

        public sealed override IReadOnlyList<T> LoadMany(TId[] ids, IMartenSession session)
        {
            var list = preselectLoadedDocuments(ids, session, out var command);
            var selector = (ISelector<T>)BuildSelector(session);

            using (var reader = session.Database.ExecuteReader(command))
            {
                while (reader.Read())
                {
                    var document = selector.Resolve(reader);
                    list.Add(document);
                }
            }

            return list;
        }

        private List<T> preselectLoadedDocuments(TId[] ids, IMartenSession session, out NpgsqlCommand command)
        {
            var list = new List<T>();

            Dictionary<TId, T> dict;
            if (session.ItemMap.TryGetValue(typeof(T), out var d))
            {
                dict = (Dictionary<TId, T>) d;
            }
            else
            {
                dict = new Dictionary<TId, T>();
                session.ItemMap.Add(typeof(TId), dict);
            }

            var idList = new List<TId>();
            foreach (var id in ids)
            {
                if (dict.TryGetValue(id, out var doc))
                {
                    list.Add(doc);
                }
                else
                {
                    idList.Add(id);
                }
            }

            command = BuildLoadManyCommand(idList.ToArray(), session.Tenant);
            return list;
        }

        public sealed override async Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session,
            CancellationToken token)
        {
            var list = preselectLoadedDocuments(ids, session, out var command);
            var selector = (ISelector<T>)BuildSelector(session);

            using (var reader = await session.Database.ExecuteReaderAsync(command, token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    var document = await selector.ResolveAsync(reader, token).ConfigureAwait(false);
                    list.Add(document);
                }
            }

            return list;
        }
    }
}
