using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.CodeGeneration;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Schema;

namespace Marten.Internal.Storage;

internal interface IQueryOnlyDocumentStorage: IDocumentStorage
{
    ISelectClause SelectClauseForIncludes();
}

public abstract class QueryOnlyDocumentStorage<T, TId>: DocumentStorage<T, TId>, IQueryOnlyDocumentStorage where TId : notnull where T : notnull
{
    public QueryOnlyDocumentStorage(DocumentMapping document): base(StorageStyle.QueryOnly, document)
    {
    }

    public ISelectClause SelectClauseForIncludes()
    {
        return new DataAndIdSelectClause<T>(this);
    }

    public sealed override void Store(IMartenSession session, T document)
    {
    }

    public sealed override void Store(IMartenSession session, T document, Guid? version)
    {
    }

    public sealed override void Store(IMartenSession session, T document, int revision)
    {
    }

    public sealed override void Eject(IMartenSession session, T document)
    {
    }

    public sealed override async Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session,
        CancellationToken token)
    {
        var list = new List<T>();

        var command = BuildLoadManyCommand(ids, session.TenantId);
        var selector = (ISelector<T>)BuildSelector(session);

        await using (var reader = await session.ExecuteReaderAsync(command, token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var document = await selector.ResolveAsync(reader, token).ConfigureAwait(false);
                list.Add(document);
            }
        }

        return list;
    }

    public sealed override Task<T?> LoadAsync(TId id, IMartenSession session, CancellationToken token)
    {
        return loadAsync(id, session, token);
    }
}
