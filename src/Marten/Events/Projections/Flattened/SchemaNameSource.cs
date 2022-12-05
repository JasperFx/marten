namespace Marten.Events.Projections.Flattened;

public enum SchemaNameSource
{
    /// <summary>
    ///     The user will supply the schema name explicitly
    /// </summary>
    Explicit,

    /// <summary>
    ///     The table should be placed in the main document schema as this IDocumentStore. (StoreOptions.DatabaseSchemaName)
    /// </summary>
    DocumentSchema,

    /// <summary>
    ///     The table should be placed in the designated schema for the events (StoreOptions.Events.DatabaseSchemaName)
    /// </summary>
    EventSchema
}
