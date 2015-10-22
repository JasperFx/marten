using System;
using System.Collections;
using System.Data;
using System.Linq;
using FubuCore;
using Marten.Generation;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class DocumentStorage<T, TKey> : IDocumentStorage
    {
        private readonly string _upsertCommand = SchemaBuilder.UpsertNameFor(typeof (T));
        private readonly string _loadCommand = "select data from {0} where id = :id".ToFormat(SchemaBuilder.TableNameFor(typeof(T)));

        private readonly string _deleteCommand =
            "delete from {0} where id = :id".ToFormat(SchemaBuilder.TableNameFor(typeof (T)));

        private readonly string _byArrayCommand =
            "select data from {0} where id = ANY(:ids)".ToFormat(SchemaBuilder.TableNameFor(typeof (T)));

        private readonly string _tableName = SchemaBuilder.TableNameFor(typeof (T));

        private readonly Func<T, TKey> _key;

        public DocumentStorage(Func<T, TKey> key)
        {
            _key = key;

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

        public string TableName
        {
            get
            {
                return _tableName;
            }
        }

        public void InitializeSchema(SchemaBuilder builder)
        {
            builder.CreateTable(typeof(T), typeof(TKey));
            builder.DefineUpsert(typeof(T), typeof(TKey));
        }

        public Type DocumentType
        {
            get { return typeof (T); }
        }
    }
}