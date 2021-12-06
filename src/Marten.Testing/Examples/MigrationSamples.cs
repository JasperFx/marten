using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Events;
using Weasel.Core.Migrations;

namespace Marten.Testing.Examples
{
    public class MigrationSamples
    {
        private async Task configure()
        {
            #region sample_configure-document-types-upfront
            using var store = DocumentStore.For(_ =>
            {
                // This is enough to tell Marten that the User
                // document is persisted and needs schema objects
                _.Schema.For<User>();

                // Lets Marten know that the event store is active
                _.Events.AddEventType(typeof(MembersJoined));
            });
            #endregion

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
