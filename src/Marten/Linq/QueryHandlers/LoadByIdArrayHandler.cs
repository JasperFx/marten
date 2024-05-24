#nullable enable
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;
using Marten.Services;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Linq.QueryHandlers;

internal class LoadByIdArrayHandler<T, TKey>: IQueryHandler<IReadOnlyList<T>> where T : notnull
{
    private readonly TKey[] _ids;
    private readonly IDocumentStorage<T> storage;

    public LoadByIdArrayHandler(IDocumentStorage<T> documentStorage, TKey[] ids)
    {
        storage = documentStorage;
        _ids = ids;
    }

    public void ConfigureCommand(ICommandBuilder sql, IMartenSession session)
    {
        sql.Append("select ");

        var fields = storage.SelectFields();

        sql.Append(fields[0]);
        for (var i = 1; i < fields.Length; i++)
        {
            sql.Append(", ");
            sql.Append(fields[i]);
        }

        sql.Append(" from ");
        sql.Append(storage.FromObject);
        sql.Append(" as d where id = ANY(");

        sql.AppendParameter(_ids);

        sql.Append(")");

        storage.AddTenancyFilter(sql, session.TenantId);
    }

    public IReadOnlyList<T> Handle(DbDataReader reader, IMartenSession session)
    {
        var list = new List<T>();

        var selector = (ISelector<T>)storage.BuildSelector(session);

        while (reader.Read())
        {
            list.Add(selector.Resolve(reader));
        }

        return list;
    }

    public async Task<IReadOnlyList<T>> HandleAsync(DbDataReader reader, IMartenSession session,
        CancellationToken token)
    {
        var list = new List<T>();

        var selector = (ISelector<T>)storage.BuildSelector(session);

        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            list.Add(await selector.ResolveAsync(reader, token).ConfigureAwait(false));
        }

        return list;
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        return reader.As<NpgsqlDataReader>().StreamMany(stream, token);
    }
}
