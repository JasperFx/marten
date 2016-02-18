using System.Threading.Tasks;

namespace Marten
{
    public interface IDocumentSessionListener
    {
        /// <summary>
        /// Called just after IDocumentSession.SaveChanges() is called, but before
        /// any database calls are made
        /// </summary>
        /// <param name="session"></param>
        void BeforeSaveChanges(IDocumentSession session);


        Task BeforeSaveChangesAsync(IDocumentSession session);


        /// <summary>
        /// After an IDocumentSession is committed
        /// </summary>
        /// <param name="session"></param>
        void AfterCommit(IDocumentSession session);

        Task AfterCommitAsync(IDocumentSession session);
    }

    public abstract class DocumentSessionListenerBase : IDocumentSessionListener
    {
        public virtual void BeforeSaveChanges(IDocumentSession session)
        {
            // Nothing
        }

        public async virtual Task BeforeSaveChangesAsync(IDocumentSession session)
        {
            await Task.Run(() => BeforeSaveChanges(session));
        }

        public virtual void AfterCommit(IDocumentSession session)
        {
            // Nothing
        }

        public virtual async Task AfterCommitAsync(IDocumentSession session)
        {
            await Task.Run(() => AfterCommit(session));
        }
    }
}