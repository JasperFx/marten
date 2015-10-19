using System;
using System.Data;
using FubuCore;
using Marten.Generation;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class DocumentStorage<T> : IDocumentStorage where T : IDocument
    {
        private readonly string _upsertCommand = SchemaBuilder.UpsertNameFor(typeof (T));
        private readonly string _loadCommand = "select data from {0} where id = :id".ToFormat(SchemaBuilder.TableNameFor(typeof(T)));

        private readonly string _deleteCommand =
            "delete from {0} where id = :id".ToFormat(SchemaBuilder.TableNameFor(typeof (T)));

        private readonly string _tableName = SchemaBuilder.TableNameFor(typeof (T));

        public NpgsqlCommand UpsertCommand(T document, string json)
        {
            var command = new NpgsqlCommand(_upsertCommand);
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.Add("docId", NpgsqlDbType.Uuid).Value = document.Id;
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

            // TODO -- someday we'll need to map the NpgsqlDbType a bit
            command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = id; 

            return command;
        }

        public NpgsqlCommand DeleteCommandForId(object id)
        {
            var command = new NpgsqlCommand(_deleteCommand);
            command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = id;

            return command;
        }

        public NpgsqlCommand DeleteCommandForEntity(object entity)
        {
            return DeleteCommandForId(entity.As<T>().Id);
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
            builder.CreateTable(typeof(T));
            builder.DefineUpsert(typeof(T));
        }

        public Type DocumentType
        {
            get { return typeof (T); }
        }
    }
}