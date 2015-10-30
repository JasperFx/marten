using System;
using System.Data;
using FubuCore;
using Marten.Generation;
using Marten.Linq;
using Npgsql;
using NpgsqlTypes;
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

        private readonly string _tableName = DocumentMapping.TableNameFor(typeof (T));
        private readonly string _upsertCommand = DocumentMapping.UpsertNameFor(typeof (T));

        public DocumentStorage(Func<T, TKey> key)
        {
            _key = key;
        }

        public Type DocumentType
        {
            get { return typeof (T); }
        }

        NpgsqlCommand IDocumentStorage.UpsertCommand(object document, string json)
        {
            return UpsertCommand(document.As<T>(), json);
        }

        public NpgsqlCommand LoaderCommand(object id)
        {
            var command = new NpgsqlCommand(_loadCommand);

            var param = new NpgsqlParameter
            {
                ParameterName = "id",
                Value = id
            };

            command.Parameters.Add(param);

            return command;
        }

        public NpgsqlCommand DeleteCommandForId(object id)
        {
            var command = new NpgsqlCommand(_deleteCommand);
            var param = new NpgsqlParameter
            {
                ParameterName = "id",
                Value = id
            };

            command.Parameters.Add(param);

            return command;
        }

        public NpgsqlCommand DeleteCommandForEntity(object entity)
        {
            return DeleteCommandForId(_key(entity.As<T>()));
        }


        public NpgsqlCommand LoadByArrayCommand<TInput>(TInput[] ids)
        {
            var command = new NpgsqlCommand(_byArrayCommand);
            var param = new NpgsqlParameter
            {
                ParameterName = "ids",
                Value = ids
            };

            command.Parameters.Add(param);

            return command;
        }

        public NpgsqlCommand AnyCommand(QueryModel queryModel)
        {
            return new DocumentQuery<T>(_tableName, queryModel).ToAnyCommand();
        }

        public NpgsqlCommand CountCommand(QueryModel queryModel)
        {
            return new DocumentQuery<T>(_tableName, queryModel).ToCountCommand();
        }

        public string TableName
        {
            get { return _tableName; }
        }

        public void InitializeSchema(SchemaBuilder builder)
        {
            var mapping = new DocumentMapping(typeof(T));
            builder.CreateTable(mapping.ToTable(null));
            builder.DefineUpsert(typeof (T), typeof (TKey));
        }

        public NpgsqlCommand UpsertCommand(T document, string json)
        {
            var command = new NpgsqlCommand(_upsertCommand)
            {
                CommandType = CommandType.StoredProcedure
            };

            command.Parameters.Add(new NpgsqlParameter
            {
                ParameterName = "id",
                Value = _key(document)
            });

            command.Parameters.Add("doc", NpgsqlDbType.Json).Value = json;

            return command;
        }
    }
}