using System;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using FubuCore;
using Marten.Generation;
using Npgsql;
using NpgsqlTypes;
using StoryTeller.Engine.UserInterface;

namespace Marten.Schema
{
    public static class DocumentStorageBuilder
    {
        public static IDocumentStorage Build(Type documentType)
        {
            var prop =
                documentType.GetProperties().Where(x => x.Name.EqualsIgnoreCase("id") && x.CanWrite).FirstOrDefault();

            if (prop == null) throw new ArgumentOutOfRangeException("documentType", "Type {0} does not have a public settable property named 'id' or 'Id'".ToFormat(documentType.FullName));

            var parameter = Expression.Parameter(documentType, "x");
            var propExpression = Expression.Property(parameter, prop);

            var lambda = Expression.Lambda(propExpression, parameter);

            var func = lambda.Compile();

            return typeof (DocumentStorage<,>).CloseAndBuildAs<IDocumentStorage>(func, documentType, prop.PropertyType);
        }


    }

    public class DocumentStorage<T, TKey> : IDocumentStorage where T : IDocument
    {
        private readonly string _upsertCommand = SchemaBuilder.UpsertNameFor(typeof (T));
        private readonly string _loadCommand = "select data from {0} where id = :id".ToFormat(SchemaBuilder.TableNameFor(typeof(T)));

        private readonly string _deleteCommand =
            "delete from {0} where id = :id".ToFormat(SchemaBuilder.TableNameFor(typeof (T)));

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