using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Shouldly;
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

        [Fact]
        public void call_listener_events_on_document_store()
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
                using (var session = store.OpenSession())
                {
                    var user1 = new User { Id = Guid.NewGuid() };
                    var user2 = new User { Id = Guid.NewGuid() };

                    session.Store(user1, user2);

                    stub1.StoredDocuments.ShouldContainKeyAndValue(user1.Id, user1);
                    stub1.StoredDocuments.ShouldContainKeyAndValue(user2.Id, user2);

                    stub2.StoredDocuments.ShouldContainKeyAndValue(user1.Id, user1);
                    stub2.StoredDocuments.ShouldContainKeyAndValue(user2.Id, user2);
                }
            }
        }

        [Fact]
        public void call_listener_events_on_document_store_objects()
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
                using (var session = store.OpenSession())
                {
                    var user1 = new User { Id = Guid.NewGuid() };
                    var user2 = new User { Id = Guid.NewGuid() };

                    session.StoreObjects(new [] { user1, user2 });

                    stub1.StoredDocuments.ShouldContainKeyAndValue(user1.Id, user1);
                    stub1.StoredDocuments.ShouldContainKeyAndValue(user2.Id, user2);

                    stub2.StoredDocuments.ShouldContainKeyAndValue(user1.Id, user1);
                    stub2.StoredDocuments.ShouldContainKeyAndValue(user2.Id, user2);
                }
            }
        }

        [Fact]
        public void call_listener_events_on_document_load()
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
                var user1 = new User { Id = Guid.NewGuid() };
                var user2 = new User { Id = Guid.NewGuid() };

                using (var session = store.OpenSession())
                {
                    session.StoreObjects(new[] { user1, user2 });
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var user = session.Load<User>(user1.Id);

                    stub1.LoadedDocuments.ShouldContainKeyAndValue(user1.Id, user);
                    stub2.LoadedDocuments.ShouldContainKeyAndValue(user1.Id, user);
                }
            }
        }

        [Fact]
        public void call_listener_events_on_document_query()
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
                var user1 = new User { Id = Guid.NewGuid() };
                var user2 = new User { Id = Guid.NewGuid() };

                using (var session = store.OpenSession())
                {
                    session.StoreObjects(new[] { user1, user2 });
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var users = session.Query<User>().ToList();

                    stub1.LoadedDocuments.ShouldContainKeyAndValue(user1.Id, users.FirstOrDefault(where => where.Id == user1.Id));
                    stub1.LoadedDocuments.ShouldContainKeyAndValue(user2.Id, users.FirstOrDefault(where => where.Id == user2.Id));

                    stub2.LoadedDocuments.ShouldContainKeyAndValue(user1.Id, users.FirstOrDefault(where => where.Id == user1.Id));
                    stub2.LoadedDocuments.ShouldContainKeyAndValue(user2.Id, users.FirstOrDefault(where => where.Id == user2.Id));
                }
            }
        }

        [Fact]
        public void call_listener_events_on_document_store_and_dirty_tracking_session()
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
                using (var session = store.DirtyTrackedSession())
                {
                    var user1 = new User { Id = Guid.NewGuid() };
                    var user2 = new User { Id = Guid.NewGuid() };

                    session.Store(user1, user2);

                    stub1.StoredDocuments.ShouldContainKeyAndValue(user1.Id, user1);
                    stub1.StoredDocuments.ShouldContainKeyAndValue(user2.Id, user2);

                    stub2.StoredDocuments.ShouldContainKeyAndValue(user1.Id, user1);
                    stub2.StoredDocuments.ShouldContainKeyAndValue(user2.Id, user2);
                }
            }
        }

        [Fact]
        public void call_listener_events_on_document_store_objects_and_dirty_tracking_session()
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
                using (var session = store.DirtyTrackedSession())
                {
                    var user1 = new User { Id = Guid.NewGuid() };
                    var user2 = new User { Id = Guid.NewGuid() };

                    session.StoreObjects(new[] { user1, user2 });

                    stub1.StoredDocuments.ShouldContainKeyAndValue(user1.Id, user1);
                    stub1.StoredDocuments.ShouldContainKeyAndValue(user2.Id, user2);

                    stub2.StoredDocuments.ShouldContainKeyAndValue(user1.Id, user1);
                    stub2.StoredDocuments.ShouldContainKeyAndValue(user2.Id, user2);
                }
            }
        }

        [Fact]
        public void call_listener_events_on_document_load_and_dirty_tracking_session()
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
                var user1 = new User { Id = Guid.NewGuid() };
                var user2 = new User { Id = Guid.NewGuid() };

                using (var session = store.OpenSession())
                {
                    session.StoreObjects(new[] { user1, user2 });
                    session.SaveChanges();
                }

                using (var session = store.DirtyTrackedSession())
                {
                    var user = session.Load<User>(user1.Id);

                    stub1.LoadedDocuments.ShouldContainKeyAndValue(user1.Id, user);
                    stub2.LoadedDocuments.ShouldContainKeyAndValue(user1.Id, user);
                }
            }
        }

        [Fact]
        public void call_listener_events_on_document_query_and_dirty_tracking_session()
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
                var user1 = new User { Id = Guid.NewGuid() };
                var user2 = new User { Id = Guid.NewGuid() };

                using (var session = store.OpenSession())
                {
                    session.StoreObjects(new[] { user1, user2 });
                    session.SaveChanges();
                }

                using (var session = store.DirtyTrackedSession())
                {
                    var users = session.Query<User>().ToList();

                    stub1.LoadedDocuments.ShouldContainKeyAndValue(user1.Id, users.FirstOrDefault(where => where.Id == user1.Id));
                    stub1.LoadedDocuments.ShouldContainKeyAndValue(user2.Id, users.FirstOrDefault(where => where.Id == user2.Id));

                    stub2.LoadedDocuments.ShouldContainKeyAndValue(user1.Id, users.FirstOrDefault(where => where.Id == user1.Id));
                    stub2.LoadedDocuments.ShouldContainKeyAndValue(user2.Id, users.FirstOrDefault(where => where.Id == user2.Id));
                }
            }
        }

    }

    public class StubDocumentSessionListener : DocumentSessionListenerBase
    {
        public override void DocumentLoaded(object id, object document)
        {
            LoadedDocuments.Add(id, document);
        }

        public IDictionary<object, object> LoadedDocuments { get; } = new Dictionary<object, object>();

        public override void DocumentAddedForStorage(object id, object document)
        {
            StoredDocuments.Add(id, document);
        }

        public IDictionary<object, object> StoredDocuments { get; } = new Dictionary<object, object>();

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