using System;
using System.Collections.Generic;
using Marten.Schema.Sequences;

namespace Marten.Schema
{
    public interface IDocumentSchema
    {
        IDocumentStorage StorageFor(Type documentType);
        IEnumerable<string> SchemaTableNames();
        string[] DocumentTables();
        IEnumerable<string> SchemaFunctionNames();

        DocumentMapping MappingFor(Type documentType);
        void EnsureStorageExists(Type documentType);
        void Alter(Action<MartenRegistry> configure);
        void Alter<T>() where T : MartenRegistry, new();
        void Alter(MartenRegistry registry);

        ISequences Sequences { get; }

        PostgresUpsertType UpsertType { get; set; }
    }
}