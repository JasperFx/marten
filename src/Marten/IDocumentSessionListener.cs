using System.Threading.Tasks;

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
        /// <returns></returns>
        Task BeforeSaveChangesAsync(IDocumentSession session);


        /// <summary>
        /// After an IDocumentSession is committed
        /// </summary>
        /// <param name="session"></param>
        void AfterCommit(IDocumentSession session);

        /// <summary>
        /// After an IDocumentSession is committed
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        Task AfterCommitAsync(IDocumentSession session);
    }
    // ENDSAMPLE

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