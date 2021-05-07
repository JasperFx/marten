using System;
using System.Threading.Tasks;
using Weasel.Postgresql;

#nullable enable

namespace Marten.Schema
{
    public interface IDocumentSchema
    {
        DdlRules DdlRules { get; }

        /// <summary>
        ///     Write the SQL script to build the database schema
        ///     objects to a file
        /// </summary>
        /// <param name="filename"></param>
        void WriteDatabaseCreationScriptFile(string filename);

        /// <summary>
        ///     Write all the SQL scripts to build the database schema, but
        ///     split by document type
        /// </summary>
        /// <param name="directory"></param>
        void WriteDatabaseCreationScriptByType(string directory);

        /// <summary>
        ///     Creates all the SQL script that would build all the database
        ///     schema objects for the configured schema
        /// </summary>
        /// <returns></returns>
        string ToDatabaseScript();

        /// <summary>
        ///     Tries to write a "patch" SQL file to upgrade the database
        ///     to the current Marten schema configuration. Also writes a corresponding
        ///     rollback file as well.
        /// </summary>
        /// <param name="filename"></param>
        Task WriteMigrationFile(string filename);

        /// <summary>
        ///     Tries to write a "patch" SQL text to upgrade the database
        ///     to the current Marten schema configuration
        /// </summary>
        /// <returns></returns>
        Task<SchemaMigration> CreateMigration();

        /// <summary>
        ///     Validates the Marten configuration of documents and transforms against
        ///     the current database schema. Will throw an exception if any differences are
        ///     detected. Useful for "environment tests"
        /// </summary>
        Task AssertDatabaseMatchesConfiguration();

        /// <summary>
        ///     Executes all detected DDL patches to the schema based on current configuration
        ///     upfront at one time
        /// </summary>
        Task ApplyAllConfiguredChangesToDatabase(AutoCreate? withAutoCreate = null);

        /// <summary>
        ///     Generate a DDL patch for one specific document type
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        Task<SchemaMigration> CreateMigration(Type documentType);

        /// <summary>
        /// Write a migration file for a single document type to the supplied file name
        /// </summary>
        /// <param name="directory"></param>
        /// <returns></returns>
        Task WriteMigrationFileByType(string directory);
    }
}
