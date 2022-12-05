using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Storage;
using Weasel.Core;

namespace Marten;

public interface IMartenStorage
{
    /// <summary>
    ///     Directly access the default database when *not* using "database per tenant" multi-tenancy
    /// </summary>
    IMartenDatabase Database { get; }

    /// <summary>
    ///     All referenced schema names by the known objects in this database
    /// </summary>
    /// <returns></returns>
    string[] AllSchemaNames();

    /// <summary>
    ///     Return an enumerable of all schema objects in dependency order
    /// </summary>
    /// <returns></returns>
    IEnumerable<ISchemaObject> AllObjects();


    /// <summary>
    ///     Return the SQL script for the entire database configuration as a single string
    /// </summary>
    /// <returns></returns>
    string ToDatabaseScript();

    /// <summary>
    ///     Write the SQL creation script to the supplied filename
    /// </summary>
    /// <param name="filename"></param>
    /// <returns></returns>
    Task WriteCreationScriptToFile(string filename);

    /// <summary>
    ///     Write the SQL creation script by feature type to the supplied directory
    /// </summary>
    /// <param name="directory"></param>
    Task WriteScriptsByType(string directory);

    /// <summary>
    ///     Apply all detected changes between configuration and the actual database to the database
    ///     THIS APPLIES TO ALL MARTEN DATABASES IN THE CASE OF "database per tenant" multi-tenancy
    /// </summary>
    /// <param name="override">If supplied, this overrides the AutoCreate threshold of this database</param>
    /// <returns></returns>
    Task ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate? @override = null);

    /// <summary>
    ///     Determine a migration for the configured database against the actual database
    ///     NOTE -- this is only valid for single tenant Marten usage
    /// </summary>
    /// <returns></returns>
    Task<SchemaMigration> CreateMigrationAsync();

    /// <summary>
    ///     Directly access the Marten database containing the named tenant id or found by the database identifier
    /// </summary>
    /// <param name="tenantIdOrDatabaseIdentifier"></param>
    /// <returns></returns>
    ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier);

    /// <summary>
    ///     A read only list of all known Marten databases addressed by this store
    /// </summary>
    /// <returns></returns>
    ValueTask<IReadOnlyList<IMartenDatabase>> AllDatabases();
}
