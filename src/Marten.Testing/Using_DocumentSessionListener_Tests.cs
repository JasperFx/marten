using System.Threading.Tasks;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing
{
    public class Using_DocumentSessionListener_Tests
    {
        [Fact]
        public void call_listener_events_on_synchronous_session_saves()
        {
            // SAMPLE: registering-a-document-session-listener
            var stub1 = new StubDocumentSessionListener();
            var stub2 = new StubDocumentSessionListener();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Listeners.Add(stub1);
                _.Listeners.Add(stub2);
            }))
            // ENDSAMPLE
            {
                using (var session = store.LightweightSession())
                {
                    session.Store(new User(), new User());

                    session.SaveChanges();

                    stub1.SaveChangesSession.ShouldBeTheSameAs(session);
                    stub1.AfterCommitSession.ShouldBeTheSameAs(session);

                    stub2.SaveChangesSession.ShouldBeTheSameAs(session);
                    stub2.AfterCommitSession.ShouldBeTheSameAs(session);
                }
            }
        }

        [Fact]
        public async Task call_listener_events_on_synchronous_session_saves_async()
        {
            var stub1 = new StubDocumentSessionListener();
            var stub2 = new StubDocumentSessionListener();

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Listeners.Add(stub1);
                _.Listeners.Add(stub2);
            }))
            {
                using (var session = store.LightweightSession())
                {
                    session.Store(new User(), new User());

                    await session.SaveChangesAsync();

                    stub1.SaveChangesSession.ShouldBeTheSameAs(session);
                    stub1.AfterCommitSession.ShouldBeTheSameAs(session);

                    stub2.SaveChangesSession.ShouldBeTheSameAs(session);
                    stub2.AfterCommitSession.ShouldBeTheSameAs(session);
                }
            }
        }
    }

    public class StubDocumentSessionListener : DocumentSessionListenerBase
    {
        public override void BeforeSaveChanges(IDocumentSession session)
        {
            SaveChangesSession = session;
        }

        public IDocumentSession SaveChangesSession { get; set; }

        public override void AfterCommit(IDocumentSession session)
        {
            AfterCommitSession = session;
        }

        public IDocumentSession AfterCommitSession { get; set; }
    }
}