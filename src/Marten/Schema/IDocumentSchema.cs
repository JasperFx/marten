using System;
using System.Collections.Generic;
using Marten.Events;
using Marten.Generation;
using Marten.Linq;
using Marten.Schema.Sequences;
using Remotion.Linq;

namespace Marten.Schema
{
    public interface IDocumentSchema
    {
        StoreOptions StoreOptions { get; }

        IDocumentStorage StorageFor(Type documentType);

        TableName[] SchemaTables();
        TableName[] DocumentTables();
        FunctionName[] SchemaFunctionNames();

        IDocumentMapping MappingFor(Type documentType);
        void EnsureStorageExists(Type documentType);

        ISequences Sequences { get; }

        IEventStoreConfiguration Events { get; }

        PostgresUpsertType UpsertType { get; }
        MartenExpressionParser Parser { get; }

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

        TableDefinition TableSchema(IDocumentMapping documentMapping);
        TableDefinition TableSchema(Type documentType);
        IEnumerable<IDocumentMapping> AllDocumentMaps();
        IResolver<T> ResolverFor<T>();

        bool TableExists(TableName table);


        DocumentQuery ToDocumentQuery(QueryModel model);
    }
}