#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.MultiTenancy;
using Marten.Events;
using Marten.Schema;
using Marten.Storage;
using Weasel.Core;

namespace Marten;

public interface IReadOnlyStoreOptions
{
    /// <summary>
    ///     Uncommonly used configuration items
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
    ///     Custom Linq query parsers applied to this DocumentStore
    /// </summary>
    IReadOnlyLinqParsing Linq { get; }

    /// <summary>
    ///     Used to validate database object name lengths against Postgresql's NAMEDATALEN property to avoid
    ///     Marten getting confused when comparing database schemas against the configuration. See
    ///     https://www.postgresql.org/docs/current/static/sql-syntax-lexical.html
    ///     for more information. This does NOT adjust NAMEDATALEN for you.
    /// </summary>
    int NameDataLength { get; }

    /// <summary>
    ///     Gets Enum values stored as either integers or strings. This is configured on your ISerializer
    /// </summary>
    EnumStorage EnumStorage { get; }

    /// <summary>
    ///     Sets the batch size for updating or deleting documents in IDocumentSession.SaveChanges() /
    ///     IUnitOfWork.ApplyChanges()
    /// </summary>
    int UpdateBatchSize { get; }

    /// <summary>
    ///     Access to information about document store tenants configured in this application
    /// </summary>
    ITenancy Tenancy { get; }

    /// <summary>
    ///     Access to the underlying Marten serializer
    /// </summary>
    /// <returns></returns>
    ISerializer Serializer();

    /// <summary>
    ///     Access to the attached Marten logger
    /// </summary>
    /// <returns></returns>
    IMartenLogger Logger();

    /// <summary>
    ///     Retrieve a list of all the currently known document types
    ///     in this Martne store
    /// </summary>
    /// <returns></returns>
    IReadOnlyList<IDocumentType> AllKnownDocumentTypes();

    /// <summary>
    ///     Finds or resolves the configuration for a given document type
    ///     If the documentType is a subclass, you will retrieve the root
    ///     parent document configuration.
    /// </summary>
    /// <param name="documentType"></param>
    /// <returns></returns>
    IDocumentType FindOrResolveDocumentType(Type documentType);

    void AssertDocumentTypeIsSoftDeleted(Type documentType);

    /// <summary>
    /// Get database schema names for configured tables
    /// </summary>
    IDocumentSchemaResolver Schema { get; }

    /// <summary>
    /// Configure tenant id behavior within this Marten DocumentStore
    /// </summary>
    TenantIdStyle TenantIdStyle { get; set; }
}
