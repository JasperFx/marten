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

            // All migration code is async now!
            await store.Storage.Database.WriteMigrationFileAsync("1.initial.sql");
            #endregion

            #region sample_ApplyAllConfiguredChangesToDatabase
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            #endregion

            #region sample_AssertDatabaseMatchesConfiguration
            await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
            #endregion
        }
    }
}
