using System;
using System.Collections.Generic;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
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

        object Identity(object document);

        NpgsqlDbType IdType { get; }


        void RegisterUpdate(UpdateBatch batch, object entity);
    }

}