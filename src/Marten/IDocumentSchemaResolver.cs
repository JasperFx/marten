#nullable enable
namespace Marten;

public interface IDocumentSchemaResolver
{
    /// <summary>
    ///     The schema name used to store the documents.
    /// </summary>
    string DatabaseSchemaName { get; }

    /// <summary>
    ///     The database schema name for event related tables. By default this
    ///     is the same schema as the document storage
    /// </summary>
    string EventsSchemaName { get; }

    /// <summary>
    ///     Find the database name of the table backing <typeparamref name="TDocument"/>. Supports documents and projections.
    /// </summary>
    /// <typeparam name="TDocument">The document or projection to look up.</typeparam>
    /// <param name="qualified" default="true">
    ///     When true (default) the qualified name is returned (schema and table name).
    ///     Otherwise only the table name is returned.
    /// </param>
    /// <returns>The name of <typeparamref name="TDocument"/> in the database.</returns>
    string For<TDocument>(bool qualified = true);

    /// <summary>
    ///     Find the database name of the table backing the events table. Supports documents and projections.
    /// </summary>
    /// <param name="qualified" default="true">
    ///     When true (default) the qualified name is returned (schema and table name).
    ///     Otherwise only the table name is returned.
    /// </param>
    /// <returns>The name of events table in the database.</returns>
    string ForEvents(bool qualified = true);

    /// <summary>
    ///     Find the database name of the table backing the event streams table. Supports documents and projections.
    /// </summary>
    /// <param name="qualified" default="true">
    ///     When true (default) the qualified name is returned (schema and table name).
    ///     Otherwise only the table name is returned.
    /// </param>
    /// <returns>The name of event streams table in the database.</returns>
    string ForStreams(bool qualified = true);

    /// <summary>
    ///     Find the database name of the table backing the event progression table. Supports documents and projections.
    /// </summary>
    /// <param name="qualified" default="true">
    ///     When true (default) the qualified name is returned (schema and table name).
    ///     Otherwise only the table name is returned.
    /// </param>
    /// <returns>The name of event progression table in the database.</returns>
    string ForEventProgression(bool qualified = true);
}
