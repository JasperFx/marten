using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Examples
{
    public class Setting_Timestamp_on_all_changes_by_base_class_Tests
    {
        [Fact]
        public void demonstration()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "setting_timestamp";
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Listeners.Add(new TimestampListener());
            }))
            {
                var doc1s = new Doc1[] { new Doc1(), new Doc1(), };
                var doc2s = new Doc2[] { new Doc2(), new Doc2(), };
                var doc3s = new Doc3[] { new Doc3(), new Doc3(), };

                using (var session = store.OpenSession())
                {
                    session.Store(doc1s);
                    session.Store(doc2s);
                    session.Store(doc3s);

                    session.SaveChanges();
                }

                doc1s.Each(x => x.Timestamp.ShouldNotBeNull());
                doc2s.Each(x => x.Timestamp.ShouldNotBeNull());
                doc3s.Each(x => x.Timestamp.ShouldNotBeNull());
            }
        }
    }

    public class TimestampListener: DocumentSessionListenerBase
    {
        public override void BeforeSaveChanges(IDocumentSession session)
        {
            var entities = session.PendingChanges.AllChangedFor<BaseEntity>();
            entities.Each(x => x.Timestamp = DateTimeOffset.UtcNow);
        }

        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            return Task.Factory.StartNew(() => BeforeSaveChanges(session));
        }
    }

    public class Doc1: BaseEntity
    {
    }

    public class Doc2: BaseEntity
    {
    }

    public class Doc3: BaseEntity
    {
    }

    public class BaseEntity
    {
        public DateTimeOffset? Timestamp { get; set; }
        public Guid Id { get; set; }
    }
}
