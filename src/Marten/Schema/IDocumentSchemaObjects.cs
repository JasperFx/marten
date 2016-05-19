using System;
using System.IO;
using Marten.Services;

namespace Marten.Schema
{
    public interface IDocumentSchemaObjects
    {
        void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, Action<string> executeSql);

        void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer);

        void RemoveSchemaObjects(IManagedConnection connection);

        void ResetSchemaExistenceChecks();
    }
}