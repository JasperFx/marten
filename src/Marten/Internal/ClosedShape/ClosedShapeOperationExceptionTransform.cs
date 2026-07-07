#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using JasperFx;
using Marten.Exceptions;

using Marten.Internal.Storage;

using System.Data.Common;

using Npgsql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Shared <c>IExceptionTransform.TryTransform</c> body for the closed-shape
/// storage operations. Mirrors <c>StorageOperation&lt;T, TId&gt;.TryTransform</c>:
/// when Postgres raises a unique-constraint violation (SQLSTATE 23505) on
/// the document's table, surface it as
/// <see cref="DocumentAlreadyExistsException"/> instead of the raw
/// command exception — both the PK conflict (caught earlier via
/// ON CONFLICT DO NOTHING + no-row-returned) and any user-defined
/// unique index hit the same code path here.
/// </summary>
internal static class ClosedShapeOperationExceptionTransform
{
    public static bool TryTransform(
        Exception original,
        string tableName,
        Type docType,
        object id,
        [NotNullWhen(true)] out Exception? transformed)
    {
        transformed = null;

        if (original is MartenCommandException m && m.InnerException is not null)
        {
            original = m.InnerException;
        }

        if (original is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            } pg
            && pg.TableName == tableName)
        {
            transformed = new DocumentAlreadyExistsException(original, docType, id);
            return true;
        }

        return false;
    }
}

/// <summary>
///     #4821 INC-4: adapter that plugs the static Postgres exception transform into the
///     descriptor-carried <see cref="IOperationExceptionTransform"/> seam the moved
///     (Weasel.Storage) closed-shape operations consume.
/// </summary>
internal sealed class MartenOperationExceptionTransform: IOperationExceptionTransform
{
    public static readonly MartenOperationExceptionTransform Instance = new();

    private MartenOperationExceptionTransform()
    {
    }

    public bool TryTransform(Exception original, string tableName, Type documentType, object id,
        out Exception? transformed)
        => ClosedShapeOperationExceptionTransform.TryTransform(original, tableName, documentType, id, out transformed);
}
