#nullable enable
using System;
using System.Data.Common;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Internal.Storage;

/// <summary>
///     #4828: the ADO/SQL-dialect strategy the (movable) <see cref="DocumentStorage{T,TId}"/>
///     hierarchy delegates its provider-specific concerns to, instead of constructing
///     <c>Npgsql*</c> types and emitting Postgres SQL directly. One implementation
///     (<see cref="PostgresStorageDialect{TId}"/>) is injected per closed <c>TId</c>, keeping the
///     storage base free of any direct Npgsql/Postgres reference so it can move to the shared
///     package (part of the #4821 extraction epic).
/// </summary>
/// <remarks>
///     Only the concerns whose result is genuinely dialect-specific live here — command/parameter
///     materialization, the id-equality filter (which carries the provider parameter type), and
///     provider error-code interpretation. The "mechanical" scalar parameter typing routes through
///     Weasel's <c>IDatabaseProvider.ToParameterType</c>, and the document-body JSON parameter is
///     already dialect-polymorphic via <see cref="IStorageSerializer.WriteToParameter(DbParameter, object?)"/>
///     (#4819). The load-many / id-array path and the metadata-binder cluster are folded in by later
///     increments.
/// </remarks>
public interface IStorageDialect
{
    /// <summary>
    ///     Build a single-document load command over the storage's prebuilt loader SQL, binding the
    ///     raw (already <c>ToRawSqlValue</c>'d) id and, for conjoined tenancy, the tenant id.
    /// </summary>
    DbCommand BuildLoadCommand(string loaderSql, object rawId, string? tenant);

    /// <summary>
    ///     Create the id-array parameter for a load-many command from the raw (already
    ///     <c>ToRawSqlValue</c>'d) id values and the .NET type of a single raw id. Postgres binds a
    ///     single array parameter (<c>= ANY($1)</c>); other dialects may shape this differently.
    /// </summary>
    DbParameter CreateIdArrayParameter(Array rawIds, Type rawSqlType);

    /// <summary>
    ///     Build a load-many command over the storage's prebuilt array-loader SQL, binding the
    ///     id-array parameter (from <see cref="CreateIdArrayParameter"/>) and, for conjoined tenancy,
    ///     the tenant id.
    /// </summary>
    DbCommand BuildLoadManyCommand(string loadArraySql, DbParameter idArrayParameter, string? tenant);

    /// <summary>
    ///     The id-equality <see cref="ISqlFragment"/> for a raw id — carries the dialect's parameter
    ///     type so the emitted predicate binds correctly.
    /// </summary>
    ISqlFragment ByIdFilter(object rawId);

    /// <summary>
    ///     True when the exception represents an "undefined table" (e.g. truncating a table that
    ///     hasn't been created yet) — the one provider error code the storage base swallows.
    /// </summary>
    bool IsUndefinedTable(Exception exception);
}
