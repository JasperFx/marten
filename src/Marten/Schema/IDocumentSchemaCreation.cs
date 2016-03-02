using System;

namespace Marten.Schema
{
    [Obsolete("Needs to go away")]
    public interface IDocumentSchemaCreation
    {
        void CreateSchema(IDocumentSchema schema, IDocumentMapping mapping, Func<bool> shouldRegenerate);
        void RunScript(string script);
    }
}