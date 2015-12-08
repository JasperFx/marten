using System;
using System.Collections.Generic;
using Marten.Events;
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

        EventGraph Events { get; }

        PostgresUpsertType UpsertType { get; set; }

        /// <summary>
        /// Write the SQL script to build the database schema
        /// objects to a file
        /// </summary>
        /// <param name="filename"></param>
        void WriteDDL(string filename);

        /// <summary>
        /// Creates all the SQL script that would build all the database
        /// schema objects for the configured schema
        /// </summary>
        /// <returns></returns>
        string ToDDL();
    }
}