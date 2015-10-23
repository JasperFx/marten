using System;
using System.Collections.Concurrent;
using FubuCore;
using Marten.Generation;

namespace Marten.Schema
{
    public class DevelopmentDocumentSchema : IDocumentSchema, IDisposable
    {
        private readonly CommandRunner _runner;
        private readonly ConcurrentDictionary<Type, IDocumentStorage> _documentTypes = new ConcurrentDictionary<Type, IDocumentStorage>(); 

        public DevelopmentDocumentSchema(IConnectionFactory connections)
        {
            _runner = new CommandRunner(connections);
        }

        public IDocumentStorage StorageFor(Type documentType)
        {
            return _documentTypes.GetOrAdd(documentType, type =>
            {
                var storage = DocumentStorageBuilder.Build(type);

                var builder = new SchemaBuilder();
                storage.InitializeSchema(builder);

                _runner.Execute(builder.ToSql());

                return storage;
            });
        }

        public void Dispose()
        {
        }
    }
}