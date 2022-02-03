using System.Threading.Tasks;
using Marten.Testing.Documents;
using Weasel.Core.Migrations;

namespace Marten.Testing.Examples
{
    public class MigrationSamples
    {
        private async Task configure()
        {
            using var store = DocumentStore.For(_ =>
            {
                // This is enough to tell Marten that the User
                // document is persisted and needs schema objects
                _.Schema.For<User>();

            });

            #region sample_WritePatch

            var migration = await store.Schema.CreateMigrationAsync();
            // All migration code is async now!
            await store.Schema.Migrator.WriteMigrationFile("1.initial.sql", migration);
            await store.Schema.WriteMigrationFileAsync("1.initial.sql");
            #endregion

            #region sample_ApplyAllConfiguredChangesToDatabase
            await store.Schema.ApplyAllConfiguredChangesToDatabaseAsync();
            #endregion

            #region sample_AssertDatabaseMatchesConfiguration
            await store.Schema.AssertDatabaseMatchesConfigurationAsync();
            #endregion
        }
    }
}
