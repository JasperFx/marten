using System;
using FubuCore;
using Marten.Linq;
using Marten.Util;
using Npgsql;
using Remotion.Linq;

namespace Marten.Schema
{

    public class DocumentStorage<T, TKey> : IDocumentStorage
    {
        private readonly string _byArrayCommand =
            "select data from {0} where id = ANY(:ids)".ToFormat(DocumentMapping.TableNameFor(typeof (T)));

        private readonly string _deleteCommand =
            "delete from {0} where id = :id".ToFormat(DocumentMapping.TableNameFor(typeof (T)));

        private readonly Func<T, TKey> _key;

        private readonly string _loadCommand =
            "select data from {0} where id = :id".ToFormat(DocumentMapping.TableNameFor(typeof (T)));

        private readonly string _upsertCommand = DocumentMapping.UpsertNameFor(typeof (T));

        public DocumentStorage(Func<T, TKey> key)
        {
            _key = key;
        }

        public Type DocumentType => typeof (T);

        public NpgsqlCommand UpsertCommand(object document, string json)
        {
            return UpsertCommand(document.As<T>(), json);
        }

        public NpgsqlCommand LoaderCommand(object id)
        {
            return new NpgsqlCommand(_loadCommand).WithParameter("id", id);
        }

        public NpgsqlCommand DeleteCommandForId(object id)
        {
            return new NpgsqlCommand(_deleteCommand).WithParameter("id", id);
        }

        public NpgsqlCommand DeleteCommandForEntity(object entity)
        {
            return DeleteCommandForId(_key(entity.As<T>()));
        }


        public NpgsqlCommand LoadByArrayCommand<TInput>(TInput[] ids)
        {
            return new NpgsqlCommand(_byArrayCommand).WithParameter("ids", ids);
        }

        public NpgsqlCommand AnyCommand(QueryModel queryModel)
        {
            return new DocumentQuery<T>(TableName, queryModel).ToAnyCommand();
        }

        public NpgsqlCommand CountCommand(QueryModel queryModel)
        {
            return new DocumentQuery<T>(TableName, queryModel).ToCountCommand();
        }

        public string TableName { get; } = DocumentMapping.TableNameFor(typeof (T));

        public NpgsqlCommand UpsertCommand(T document, string json)
        {
            return new NpgsqlCommand(_upsertCommand)
                .AsSproc()
                .WithParameter("id", _key(document))
                .WithJsonParameter("doc", json);
        }
    }
}