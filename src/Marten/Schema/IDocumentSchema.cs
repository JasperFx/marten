using System;
using System.Collections.Generic;
using Marten.Events;
using Marten.Generation;
using Marten.Schema.Sequences;

namespace Marten.Schema
{
    public interface IDocumentSchema
    {
        IDocumentStorage StorageFor(Type documentType);
        IEnumerable<string> SchemaTableNames();
        string[] DocumentTables();
        IEnumerable<string> SchemaFunctionNames();

        IDocumentMapping MappingFor(Type documentType);
        void EnsureStorageExists(Type documentType);

        [Obsolete("Having all this done on StoreOptions now")]
        void Alter(Action<MartenRegistry> configure);
        [Obsolete("Having all this done on StoreOptions now")]
        void Alter<T>() where T : MartenRegistry, new();
        [Obsolete("Having all this done on StoreOptions now")]
        void Alter(MartenRegistry registry);

        ISequences Sequences { get; }

        IEventStoreConfiguration Events { get; }

        PostgresUpsertType UpsertType { get; }

        /// <summary>
        /// Write the SQL script to build the database schema
        /// objects to a file
        /// </summary>
        /// <param name="filename"></param>
        void WriteDDL(string filename);


        /// <summary>
        /// Write all the SQL scripts to build the database schema, but
        /// split by document type
        /// </summary>
        /// <param name="directory"></param>
        void WriteDDLByType(string directory);

        /// <summary>
        /// Creates all the SQL script that would build all the database
        /// schema objects for the configured schema
        /// </summary>
        /// <returns></returns>
        string ToDDL();

        TableDefinition TableSchema(string tableName);
        TableDefinition TableSchema(Type documentType);
        IEnumerable<IDocumentMapping> AllDocumentMaps();
    }
}