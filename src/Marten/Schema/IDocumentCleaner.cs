#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Schema;

public interface IDocumentCleaner
{
    /// <summary>
    ///     Deletes all existing document data in the underlying Postgresql database
    /// </summary>
    [Obsolete("Use async method instead.")]
    void DeleteAllDocuments();

    /// <summary>
    ///     Deletes all existing document data in the underlying Postgresql database
    /// </summary>
    Task DeleteAllDocumentsAsync(CancellationToken ct = default);

    /// <summary>
    ///     Deletes all the existing document data for the specified document type
    /// </summary>
    /// <param name="documentType"></param>
    [Obsolete("Use async method instead.")]
    void DeleteDocumentsByType(Type documentType);

    /// <summary>
    ///     Deletes all the existing document data for the specified document type
    /// </summary>
    /// <param name="documentType"></param>
    Task DeleteDocumentsByTypeAsync(Type documentType, CancellationToken ct = default);

    /// <summary>
    ///     Delete all document data *except* for the specified document types.
    /// </summary>
    /// <param name="documentTypes"></param>
    [Obsolete("Use async method instead.")]
    void DeleteDocumentsExcept(params Type[] documentTypes);


    /// <summary>
    ///     Delete all document data *except* for the specified document types.
    /// </summary>
    /// <param name="documentTypes"></param>
    Task DeleteDocumentsExceptAsync(params Type[] documentTypes) => DeleteDocumentsExceptAsync(default, documentTypes);


    /// <summary>
    ///     Delete all document data *except* for the specified document types.
    /// </summary>
    /// <param name="documentTypes"></param>
    Task DeleteDocumentsExceptAsync(CancellationToken ct, params Type[] documentTypes);

    /// <summary>
    ///     Drop all the schema objects in the underlying Postgresql database for the specified
    ///     document type
    /// </summary>
    /// <param name="documentType"></param>
    [Obsolete("Use async method instead.")]
    void CompletelyRemove(Type documentType);

    /// <summary>
    ///     Drop all the schema objects in the underlying Postgresql database for the specified
    ///     document type
    /// </summary>
    /// <param name="documentType"></param>
    Task CompletelyRemoveAsync(Type documentType, CancellationToken ct = default);

    /// <summary>
    ///     Remove all Marten-related schema objects from the underlying Postgresql database
    /// </summary>
    [Obsolete("Use async method instead.")]
    void CompletelyRemoveAll();

    /// <summary>
    ///     Remove all Marten-related schema objects from the underlying Postgresql database
    /// </summary>
    Task CompletelyRemoveAllAsync(CancellationToken ct = default);

    /// <summary>
    ///     Completely deletes all the event and stream data
    /// </summary>
    [Obsolete("Use async method instead.")]
    void DeleteAllEventData();

    /// <summary>
    ///     Completely deletes all the event and stream data
    /// </summary>
    Task DeleteAllEventDataAsync(CancellationToken ct = default);

    /// <summary>
    ///     Deletes all stream and event data for the designated streamId. Will
    ///     not impact projected documents. USE WITH CAUTION!
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="tenantId">Optional tenant id for conjoined multi-tenancy</param>
    [Obsolete("Use async method instead.")]
    void DeleteSingleEventStream(Guid streamId, string? tenantId = null);

    /// <summary>
    ///     Deletes all stream and event data for the designated streamId. Will
    ///     not impact projected documents. USE WITH CAUTION!
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="tenantId">Optional tenant id for conjoined multi-tenancy</param>
    Task DeleteSingleEventStreamAsync(Guid streamId, string? tenantId = null, CancellationToken ct = default);

    /// <summary>
    ///     Deletes all stream and event data for the designated streamId. Will
    ///     not impact projected documents. USE WITH CAUTION!
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="tenantId">Optional tenant id for conjoined multi-tenancy</param>
    [Obsolete("Use async method instead.")]
    void DeleteSingleEventStream(string streamId, string? tenantId = null);

    /// <summary>
    ///     Deletes all stream and event data for the designated streamId. Will
    ///     not impact projected documents. USE WITH CAUTION!
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="tenantId">Optional tenant id for conjoined multi-tenancy</param>
    Task DeleteSingleEventStreamAsync(string streamId, string? tenantId = null, CancellationToken ct = default);
}
