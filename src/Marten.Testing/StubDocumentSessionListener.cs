using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Testing
{
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

        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            SaveChangesSession = session;
            return Task.CompletedTask;
        }

        public override Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        {
            LastCommit = commit.Clone();
            AfterCommitSession = session;
            return Task.CompletedTask;
        }

        public IChangeSet LastCommit { get; set; }

        public IDocumentSession SaveChangesSession { get; set; }

        public override void AfterCommit(IDocumentSession session, IChangeSet commit)
        {
            LastCommit = commit.Clone();
            AfterCommitSession = session;
        }

        public IDocumentSession AfterCommitSession { get; set; }
    }
}
