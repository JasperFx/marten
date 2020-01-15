using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten
{
    // SAMPLE: IDocumentSessionListener
    public interface IDocumentSessionListener
    {
        /// <summary>
        /// Called just after IDocumentSession.SaveChanges() is called, but before
        /// any database calls are made
        /// </summary>
        /// <param name="session"></param>
        void BeforeSaveChanges(IDocumentSession session);

        /// <summary>
        /// Called just after IDocumentSession.SaveChanges() is called,
        /// but before any database calls are made
        /// </summary>
        /// <param name="session"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token);

        /// <summary>
        /// After an IDocumentSession is committed
        /// </summary>
        /// <param name="session"></param>
        /// <param name="commit"></param>
        void AfterCommit(IDocumentSession session, IChangeSet commit);

        /// <summary>
        /// After an IDocumentSession is committed
        /// </summary>
        /// <param name="session"></param>
        /// <param name="commit"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token);

        /// <summary>
        /// Called after a document is loaded
        /// </summary>
        void DocumentLoaded(object id, object document);

        /// <summary>
        /// Called after a document is explicitly added to a session
        /// as a staged insert or update
        /// </summary>
        void DocumentAddedForStorage(object id, object document);
    }

    // ENDSAMPLE

    public abstract class DocumentSessionListenerBase: IDocumentSessionListener
    {
        public virtual void BeforeSaveChanges(IDocumentSession session)
        {
            // Nothing
        }

        public virtual Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            // Nothing
            return Task.CompletedTask;
        }

        public virtual void AfterCommit(IDocumentSession session, IChangeSet commit)
        {
            // Nothing
        }

        public virtual Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        {
            // Nothing
            return Task.CompletedTask;
        }

        public virtual void DocumentLoaded(object id, object document)
        {
            // Nothing
        }

        public virtual void DocumentAddedForStorage(object id, object document)
        {
            // Nothing
        }
    }
}
