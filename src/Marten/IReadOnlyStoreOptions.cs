using System;
using System.Collections.Generic;
using Marten.Events;
using Marten.Schema;
using Marten.Storage;
using Marten.Transforms;
using Weasel.Postgresql;

#nullable enable
namespace Marten
{
    public interface IReadOnlyStoreOptions
    {
        /// <summary>
        /// Uncommonly used configuration items
        /// </summary>
        IReadOnlyAdvancedOptions Advanced { get; }

        /// <summary>
        ///     Sets the database default schema name used to store the documents.
        /// </summary>
        string DatabaseSchemaName { get; set; }

        /// <summary>
        ///     Configuration of event streams and projections
        /// </summary>
        IReadOnlyEventStoreOptions Events { get; }

        /// <summary>
        /// Custom Linq query parsers applied to this DocumentStore
        /// </summary>
        IReadOnlyLinqParsing Linq { get; }

        /// <summary>
        /// Custom transform functions configured in this document store
        /// </summary>
        /// <returns></returns>
        IReadOnlyList<TransformFunction> Transforms();

        /// <summary>
        ///     Used to validate database object name lengths against Postgresql's NAMEDATALEN property to avoid
        ///     Marten getting confused when comparing database schemas against the configuration. See
        ///     https://www.postgresql.org/docs/current/static/sql-syntax-lexical.html
        ///     for more information. This does NOT adjust NAMEDATALEN for you.
        /// </summary>
        int NameDataLength { get; }

        /// <summary>
        /// Gets Enum values stored as either integers or strings. This is configured on your ISerializer
        /// </summary>
        EnumStorage EnumStorage { get; }

        /// <summary>
        ///     Sets the batch size for updating or deleting documents in IDocumentSession.SaveChanges() /
        ///     IUnitOfWork.ApplyChanges()
        /// </summary>
        int UpdateBatchSize { get; }

        /// <summary>
        /// Access to information about document store tenants configured in this application
        /// </summary>
        ITenancy Tenancy { get; }

        /// <summary>
        /// Are the features that depend on PLV8 being used by this document store?
        /// </summary>
        bool PLV8Enabled { get; }

        /// <summary>
        /// Access to the underlying Marten serializer
        /// </summary>
        /// <returns></returns>
        ISerializer Serializer();

        /// <summary>
        /// Access to the attached Marten logger
        /// </summary>
        /// <returns></returns>
        IMartenLogger Logger();

        /// <summary>
        /// Access to the configured retry policy
        /// </summary>
        /// <returns></returns>
        IRetryPolicy RetryPolicy();

        /// <summary>
        /// Retrieve a list of all the currently known document types
        /// in this Martne store
        /// </summary>
        /// <returns></returns>
        IReadOnlyList<IDocumentType> AllKnownDocumentTypes();

        /// <summary>
        /// Finds or resolves the configuration for a given document type
        /// If the documentType is a subclass, you will retrieve the root
        /// parent document configuration.
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        IDocumentType FindOrResolveDocumentType(Type documentType);
    }
}
