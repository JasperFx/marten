#nullable enable
using System.Linq;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M15): shared helpers for the four closed-shape storage
/// classes — projecting strong-typed id arrays onto the inner primitive
/// before binding, looking up the right Npgsql array type. Kept in one
/// place so each storage class doesn't repeat the LINQ projection.
/// </summary>
internal static class ClosedShapeIdHelpers
{
    public static NpgsqlParameter BuildManyIdParameter<TDoc, TId>(TId[] ids,
        IIdentification<TDoc, TId> identification)
        where TDoc : notnull
        where TId : notnull
    {
        // For non-wrapper types ToRawSqlValue is identity, so the array
        // projection degenerates to the same shape codegen emitted.
        var raw = ids.Select(id => identification.ToRawSqlValue(id)).ToArray();
        var innerDbType = PostgresqlProvider.Instance.ToParameterType(identification.RawSqlType);
        return new NpgsqlParameter
        {
            Value = raw,
            NpgsqlDbType = NpgsqlDbType.Array | innerDbType
        };
    }
}
