using System;
using Npgsql;
using Remotion.Linq;

namespace Marten.Schema
{
    public interface IDocumentStorage
    {
        NpgsqlCommand UpsertCommand(object document, string json);
        NpgsqlCommand LoaderCommand(object id);
        NpgsqlCommand DeleteCommandForId(object id);
        NpgsqlCommand DeleteCommandForEntity(object entity);
        string TableName { get; }

        NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids);
        NpgsqlCommand AnyCommand(QueryModel queryModel);
        NpgsqlCommand CountCommand(QueryModel queryModel);

        Type DocumentType { get; }
    }

}