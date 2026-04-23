#nullable enable
using System;
using System.Collections;
using System.Linq;
using Marten.Linq.Members;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class CollectionIsOneOfFilter: ISqlFragment
{
    private readonly ICollectionMember _collectionMember;
    private readonly object _values;

    public CollectionIsOneOfFilter(ICollectionMember collectionMember, object values)
    {
        _collectionMember = collectionMember;
        _values = normalizeValues(values, collectionMember.ElementType);
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_collectionMember.ArrayLocator);
        builder.Append(" && ");
        builder.AppendParameter(_values, dbTypeFor(_collectionMember.ElementType));
    }

    private static object normalizeValues(object values, Type elementType)
    {
        if (values is Array { Length: 1 } array && array.GetType().GetElementType() != elementType)
        {
            values = array.GetValue(0)!;
        }

        if (values is not string && values is IEnumerable enumerable)
        {
            var raw = enumerable.Cast<object>().ToArray();
            var typed = Array.CreateInstance(elementType, raw.Length);

            for (var i = 0; i < raw.Length; i++)
            {
                typed.SetValue(raw[i], i);
            }

            return typed;
        }

        return values;
    }

    private static NpgsqlDbType dbTypeFor(Type elementType)
    {
        return NpgsqlDbType.Array | (elementType == typeof(string)
            ? NpgsqlDbType.Varchar
            : PostgresqlProvider.Instance.ToParameterType(elementType));
    }
}
