using System;

namespace Marten.Schema
{
    public interface IDocumentSchemaCreation
    {
        void CreateSchema(IDocumentSchema schema, IDocumentMapping mapping, Func<bool> shouldRegenerate);
        void RunScript(string script);
    }
}