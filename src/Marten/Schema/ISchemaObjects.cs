using System;
using System.IO;
using Marten.Services;

namespace Marten.Schema
{
    public interface ISchemaObjects
    {
        void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, IDDLRunner runner);

        void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer);

        void RemoveSchemaObjects(IManagedConnection connection);

        void ResetSchemaExistenceChecks();

        void WritePatch(IDocumentSchema schema, IDDLRunner runner);

        string Name { get; }
    }
}