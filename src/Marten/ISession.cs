
namespace Marten
{
    /// <summary>
    /// Interface for a Marten session. This session does not support tracking of changes of loaded documents. 
    /// This means that in order to insert/update documents on SaveChanged you should explicitly call the Store(document) 
    /// method of the session.
    /// </summary>
    public interface ISession : IDocumentSession
    {
    }
}