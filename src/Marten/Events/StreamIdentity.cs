#nullable enable
namespace Marten.Events
{
    /// <summary>
    /// Specify the identity strategy for event streams
    /// </summary>
    public enum StreamIdentity
    {
        /// <summary>
        /// Streams should be identified by Guid
        /// </summary>
        AsGuid,

        /// <summary>
        /// Streams should be identified by a user supplied string
        /// </summary>
        AsString
    }
}
