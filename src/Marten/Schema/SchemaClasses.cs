using System;
using Marten.Generation;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Schema
{

    public interface IDocumentSchema
    {
        IDocumentStorage StorageFor(Type documentType);
    }

    public class DevelopmentDocumentSchema : IDocumentSchema, IDisposable
    {
        private readonly IConnectionFactory _connections;
        private readonly Lazy<CommandRunner> _runner;

        public DevelopmentDocumentSchema(IConnectionFactory connections)
        {
            _connections = connections;
            _runner = new Lazy<CommandRunner>(() => new CommandRunner(connections.Create()));
        }

        public IDocumentStorage StorageFor(Type documentType)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            if (_runner.IsValueCreated) _runner.Value.Dispose();
        }
    }

    public interface IDocumentCleaner
    {
        void AllDocuments();
        void DocumentsFor(Type documentType);
        void DocumentsExcept(params Type[] documentTypes);

        void CompletelyRemove(Type documentType);
    }

    public class DevelopmentDocumentCleaner : IDocumentCleaner
    {
        public void AllDocuments()
        {
            throw new NotImplementedException();
        }

        public void DocumentsFor(Type documentType)
        {
            throw new NotImplementedException();
        }

        public void DocumentsExcept(params Type[] documentTypes)
        {
            throw new NotImplementedException();
        }

        public void CompletelyRemove(Type documentType)
        {
            throw new NotImplementedException();
        }
    }

    public interface IDocumentStorage
    {
        
    }

    public interface IDocumentStorage<T> : IDocumentStorage where T : IDocument
    {
        // Later
        //DataTable CreateTable();
        //NpgsqlCommand UpsertCommand();

        NpgsqlCommand UpsertCommand(T document, string json);
        NpgsqlCommand LoaderCommand(object id);

        void InitializeSchema(SchemaBuilder builder);
    }

    public class DocumentStorage<T> : IDocumentStorage<T> where T : IDocument
    {
        private readonly string _upsertCommand = SchemaBuilder.UpsertNameFor(typeof (T));
        private readonly string _loadCommand = "select data from {0} where id = :id";

        public NpgsqlCommand UpsertCommand(T document, string json)
        {
            var command = new NpgsqlCommand(_upsertCommand);
            command.Parameters.Add("docId", NpgsqlDbType.Uuid).Value = document.Id;
            command.Parameters.Add("doc", NpgsqlDbType.Json).Value = json;

            return command;
        }

        public NpgsqlCommand LoaderCommand(object id)
        {
            var command = new NpgsqlCommand(_loadCommand);
            command.Parameters.Add("id", NpgsqlDbType.Uuid);

            return command;
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