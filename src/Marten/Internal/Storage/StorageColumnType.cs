#nullable enable

namespace Marten.Internal.Storage;

/// <summary>
///     #4828: the small, dialect-neutral vocabulary of logical column types the metadata binders
///     use when contributing a value to the bulk-load path. A dialect maps each to its own provider
///     parameter type (Postgres: <c>NpgsqlDbType</c>; SQL Server: <c>SqlDbType</c>). This is the
///     seam that lets the fixed Marten metadata binders drop their direct
///     <c>NpgsqlBinaryImporter</c>/<c>NpgsqlDbType</c> dependency so they can move to a shared package.
/// </summary>
/// <remarks>
///     Deliberately covers only the fixed metadata columns' types. User-defined duplicated fields
///     carry an arbitrary, user-overridable provider type (<see cref="Linq.Members.DuplicatedField.DbType"/>)
///     that cannot be reduced to this set, so their bulk writing stays on the Postgres-native path.
/// </remarks>
public enum StorageColumnType
{
    String,
    Guid,
    Long,
    Int,
    Boolean,
    Timestamp,
    Json
}
