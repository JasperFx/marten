using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Storage;
using Weasel.Core;

namespace Marten;

public partial class DocumentStore: IMartenStorage
{
    public IMartenStorage Storage => this;

    string[] IMartenStorage.AllSchemaNames()
    {
        return nulloDatabase().AllSchemaNames();
    }

    IEnumerable<ISchemaObject> IMartenStorage.AllObjects()
    {
        return nulloDatabase().AllObjects();
    }

    string IMartenStorage.ToDatabaseScript()
    {
        return nulloDatabase().ToDatabaseScript();
    }

    Task IMartenStorage.WriteCreationScriptToFile(string filename)
    {
        return nulloDatabase().WriteCreationScriptToFile(filename);
    }

    Task IMartenStorage.WriteScriptsByType(string directory)
    {
        return nulloDatabase().WriteCreationScriptToFile(directory);
    }

    async Task IMartenStorage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate? @override)
    {
        var databases = await Tenancy.BuildDatabases().ConfigureAwait(false);

        await Parallel.ForEachAsync(databases,
                async (d, token) => await d.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    async ValueTask<IReadOnlyList<IMartenDatabase>> IMartenStorage.AllDatabases()
    {
        var databases = await Tenancy.BuildDatabases().ConfigureAwait(false);
        return databases.OfType<IMartenDatabase>().ToList();
    }

    Task<SchemaMigration> IMartenStorage.CreateMigrationAsync()
    {
        return Tenancy.Default.Database.CreateMigrationAsync();
    }

    IMartenDatabase IMartenStorage.Database => Tenancy.Default.Database;

    ValueTask<IMartenDatabase> IMartenStorage.FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier)
    {
        return Tenancy.FindOrCreateDatabase(tenantIdOrDatabaseIdentifier);
    }

    private MartenDatabase nulloDatabase()
    {
        return new MartenDatabase(Options, new ConnectionFactory(string.Empty), "NULLO");
    }
}
