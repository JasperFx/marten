using System;
using System.Collections.Generic;
using Marten.Schema;

namespace Marten.Storage
{
    public class TenantSchema : IDocumentSchema
    {
        private readonly StorageFeatures _features;
        private readonly IConnectionFactory _factory;

        public TenantSchema(StoreOptions options, IConnectionFactory factory)
        {
            _features = options.Storage;
            _factory = factory;
            StoreOptions = options;
        }

        public StoreOptions StoreOptions { get; }
        public void WriteDDL(string filename)
        {
            throw new NotImplementedException();
        }

        public void WriteDDLByType(string directory)
        {
            throw new NotImplementedException();
        }

        public string ToDDL()
        {
            throw new NotImplementedException();
        }

        public IDbObjects DbObjects { get; }
        public void WritePatch(string filename, bool withSchemas = true)
        {
            throw new NotImplementedException();
        }

        public SchemaPatch ToPatch(bool withSchemas = true)
        {
            throw new NotImplementedException();
        }

        public void AssertDatabaseMatchesConfiguration()
        {
            throw new NotImplementedException();
        }

        public void ApplyAllConfiguredChangesToDatabase()
        {
            throw new NotImplementedException();
        }

        public void EnsureFunctionExists(string functionName)
        {
            throw new NotImplementedException();
        }

        public SchemaPatch ToPatch(Type documentType)
        {
            throw new NotImplementedException();
        }

        public void WritePatchByType(string directory)
        {
            throw new NotImplementedException();
        }
    }
}