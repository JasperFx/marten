using System.Data;

namespace Marten.Services
{
    public sealed  class SessionOptions
    {
        /// <summary>
        /// Default to DocumentTracking.IdentityOnly
        /// </summary>
        public readonly DocumentTracking Tracking;

        /// <summary>
        /// Default to 30 seconds
        /// </summary>
        public readonly int Timeout;

        /// <summary>
        /// Default to IsolationLevel.ReadCommitted
        /// </summary>
        public readonly IsolationLevel IsolationLevel;

        public SessionOptions(DocumentTracking tracking = DocumentTracking.IdentityOnly, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, int timeout = 30)
        {
            Tracking = tracking;
            Timeout = timeout;
            IsolationLevel = isolationLevel;
        }
    }
}