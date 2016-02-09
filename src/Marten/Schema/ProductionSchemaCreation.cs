using System;
using Baseline;

namespace Marten.Schema
{
    public class ProductionSchemaCreation : IDocumentSchemaCreation
    {
        public void CreateSchema(IDocumentSchema schema, IDocumentMapping mapping, Func<bool> shouldRegenerate)
        {
            if (shouldRegenerate())
            {
                var className = nameof(StoreOptions);
                var propName = nameof(StoreOptions.AutoCreateSchemaObjects);

                string message = $"No document storage exists for type {mapping.DocumentType.FullName} and cannot be created dynamically unless the {className}.{propName} = true. See http://jasperfx.github.io/marten/documentation/documents/ for more information";
                throw new InvalidOperationException(message);
            }
        }

        public void RunScript(string script)
        {
            throw new InvalidOperationException("Running DDL scripts are prohibited in production mode");
        }
    }
}