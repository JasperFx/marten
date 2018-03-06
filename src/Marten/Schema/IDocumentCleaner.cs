using System;

namespace Marten.Schema
{
    public interface IDocumentCleaner
    {
        /// <summary>
        /// Deletes all existing document data in the underlying Postgresql database
        /// </summary>
        void DeleteAllDocuments();

        /// <summary>
        /// Deletes all the existing document data for the specified document type
        /// </summary>
        /// <param name="documentType"></param>
        void DeleteDocumentsFor(Type documentType);

        /// <summary>
        /// Delete all document data *except* for the specified document types. 
        /// </summary>
        /// <param name="documentTypes"></param>
        void DeleteDocumentsExcept(params Type[] documentTypes);

        /// <summary>
        /// Drop all the schema objects in the underlying Postgresql database for the specified
        /// document type
        /// </summary>
        /// <param name="documentType"></param>
        void CompletelyRemove(Type documentType);

        /// <summary>
        /// Remove all Marten-related schema objects from the underlying Postgresql database
        /// </summary>
        void CompletelyRemoveAll();

        /// <summary>
        /// Completely deletes all the event and stream data
        /// </summary>
        void DeleteAllEventData();

        /// <summary>
        /// Deletes all stream and event data for the designated streamId. Will
        /// not impact projected documents. USE WITH CAUTION!
        /// </summary>
        /// <param name="streamId"></param>
        void DeleteSingleEventStream(Guid streamId);

        /// <summary>
        /// Deletes all stream and event data for the designated streamId. Will
        /// not impact projected documents. USE WITH CAUTION!
        /// </summary>
        /// <param name="streamId"></param>
        void DeleteSingleEventStream(string streamId);
    }
}