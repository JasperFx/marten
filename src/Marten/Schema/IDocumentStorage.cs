using System;
using System.Collections.Generic;
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
        NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids);
        Type DocumentType { get; }
    }

}