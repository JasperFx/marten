namespace Marten.Metadata
{
    /// <summary>
    /// Optionally implement this interface to add correlation
    /// tracking to your Marten document type with the tracking
    /// information available on the documents themselves
    /// </summary>
    public interface ITracked
    {
        /// <summary>
        /// Metadata describing the correlation id for the
        /// last system activity to edit this document
        /// </summary>
        string CorrelationId {get;set;}

        /// <summary>
        /// Metadata describing the causation id for the
        /// last system activity to edit this document
        /// </summary>
        string CausationId {get;set;}

        /// <summary>
        /// Metadata describing the user who last modified
        /// this document
        /// </summary>
        string LastModifiedBy {get;set;}
    }
}
