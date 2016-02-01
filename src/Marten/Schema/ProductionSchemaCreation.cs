using System;
using Baseline;

namespace Marten.Schema
{
    public class ProductionSchemaCreation : IDocumentSchemaCreation
    {
        public void CreateSchema(IDocumentSchema schema, DocumentMapping mapping)
        {
            var className = nameof(StoreOptions);
            var propName = nameof(StoreOptions.AutoCreateSchemaObjects);

            throw new InvalidOperationException($"No document storage exists for type {mapping.DocumentType.FullName} and cannot be created dynamically unless the {className}.{propName} = true. See http://jasperfx.github.io/marten/documentation/documents/ for more information");
        }

        public void RunScript(string script)
        {
            throw new InvalidOperationException("Running DDL scripts are prohibited in production mode");
        }
    }
}