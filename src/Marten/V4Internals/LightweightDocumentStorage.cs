using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;

namespace Marten.V4Internals
{
    public abstract class LightweightDocumentStorage<T, TId>: DocumentStorage<T, TId>
    {
        public LightweightDocumentStorage(DocumentMapping document) : base(document)
        {
        }

        public sealed override void Store(IMartenSession session, T document)
        {
            AssignIdentity(document, session.Tenant);
        }

        public sealed override void Store(IMartenSession session, T document, Guid? version)
        {
            var identity = AssignIdentity(document, session.Tenant);

            if (version.HasValue)
            {
                session.Versions.StoreVersion<T, TId>(identity, version.Value);
            }
            else
            {
                session.Versions.ClearVersion<T, TId>(identity);
            }


        }

        public sealed override void Eject(IMartenSession session, T document)
        {
            // Nothing!
        }

        public sealed override IReadOnlyList<T> LoadMany(TId[] ids, IMartenSession session)
        {
            var list = new List<T>();

            var command = BuildLoadManyCommand(ids, session.Tenant);
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

        public sealed override async Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session,
            CancellationToken token)
        {
            var list = new List<T>();

            var command = BuildLoadManyCommand(ids, session.Tenant);
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
