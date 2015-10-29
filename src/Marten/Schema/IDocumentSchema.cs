using System;
using System.Collections.Generic;

namespace Marten.Schema
{
    public interface IDocumentSchema
    {
        IDocumentStorage StorageFor(Type documentType);
        IEnumerable<string> SchemaTableNames();
        string[] DocumentTables();
        IEnumerable<string> SchemaFunctionNames();
    }
}