using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Schema
{
    public interface IDocumentCleaner
    {
        /// <summary>
        /// Deletes all existing document data in the underlying Postgresql database
        /// </summary>
        void DeleteAllDocuments();

        /// <summary>
        /// Deletes all existing document data in the underlying Postgresql database
        /// </summary>
        Task DeleteAllDocumentsAsync(CancellationToken token = default(CancellationToken));

        /// <summary>
        /// Deletes all the existing document data for the specified document type
        /// </summary>
        /// <param name="documentType"></param>
        void DeleteDocumentsFor(Type documentType);

        /// <summary>
        /// Deletes all the existing document data for the specified document type
        /// </summary>
        /// <param name="documentType"></param>
        /// <param name="token"></param>
        Task DeleteDocumentsForAsync(Type documentType, CancellationToken token = default(CancellationToken));

        /// <summary>
        /// Delete all document data *except* for the specified document types. 
        /// </summary>
        /// <param name="documentTypes"></param>
        void DeleteDocumentsExcept(params Type[] documentTypes);

        /// <summary>
        /// Delete all document data *except* for the specified document types. 
        /// </summary>
        /// <param name="documentTypes"></param>
        Task DeleteDocumentsExceptAsync(CancellationToken token = default(CancellationToken), params Type[] documentTypes);

        /// <summary>
        /// Drop all the schema objects in the underlying Postgresql database for the specified
        /// document type
        /// </summary>
        /// <param name="documentType"></param>
        void CompletelyRemove(Type documentType);

        /// <summary>
        /// Drop all the schema objects in the underlying Postgresql database for the specified
        /// document type
        /// </summary>
        /// <param name="documentType"></param>
        Task CompletelyRemoveAsync(Type documentType, CancellationToken token = default(CancellationToken));

        /// <summary>
        /// Remove all Marten-related schema objects from the underlying Postgresql database
        /// </summary>
        void CompletelyRemoveAll();

        /// <summary>
        /// Remove all Marten-related schema objects from the underlying Postgresql database
        /// </summary>
        Task CompletelyRemoveAllAsync(CancellationToken token = default(CancellationToken));

        /// <summary>
        /// Completely deletes all the event and stream data
        /// </summary>
        void DeleteAllEventData();

        /// <summary>
        /// Completely deletes all the event and stream data
        /// </summary>
        Task DeleteAllEventDataAsync(CancellationToken token = default(CancellationToken));
    }
}