using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;

namespace EventSourcingTests.Examples;

public class MigrationSamples
{
    private static async Task configure()
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

    }
}