using System;
using FubuCore;

namespace Marten.Schema
{
    public class ProductionSchemaCreation : IDocumentSchemaCreation
    {
        public void CreateSchema(IDocumentStorage storage)
        {
            throw new InvalidOperationException("No document storage exists for type {0} and cannot be created dynamically in production mode".ToFormat(storage.DocumentType.FullName));
        }
    }
}