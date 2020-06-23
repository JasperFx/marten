using System;
using Marten.Schema.Identity.Sequences;
using Npgsql;

namespace Marten.V4Internals
{
    public interface ITenant
    {
        string TenantId { get; }

        /// <summary>
        /// Used to create new Hilo sequences
        /// </summary>
        ISequences Sequences { get; }


        /// <summary>
        /// Fetch a connection to the tenant database
        /// </summary>
        /// <returns></returns>
        NpgsqlConnection CreateConnection();
    }

    public interface ITenantStorage
    {
        /// <summary>
        /// Directs Marten to disregard any previous schema checks. Useful
        /// if you change the underlying schema without shutting down the document store
        /// </summary>
        void ResetSchemaExistenceChecks();

        void MarkAllFeaturesAsChecked();

        /// <summary>
        /// Ensures that the IDocumentStorage object for a document type is ready
        /// and also attempts to update the database schema for any detected changes
        /// </summary>
        /// <param name="documentType"></param>
        void EnsureStorageExists(Type documentType);
    }
}
