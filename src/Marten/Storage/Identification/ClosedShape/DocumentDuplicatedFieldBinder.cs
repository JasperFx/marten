#nullable enable
using System;
using System.Data.Common;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Linq.Members;
using Npgsql;
using Weasel.Core;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M10): <see cref="IDocumentMetadataBinder{TDoc}"/> for a
/// <see cref="DuplicatedField"/> on the document mapping. Extracts the
/// configured member value from the document via null-safe walk over
/// <see cref="DuplicatedField"/>.<c>Members</c> and binds it to the
/// duplicated column. The canonical value still lives in the
/// <c>data</c> JSON column — duplicated columns are write-only mirrors
/// used for indexing / WHERE-clause pushdown.
/// </summary>
/// <remarks>
/// Read path is a no-op: <see cref="DuplicatedFieldColumn"/> is not
/// <c>ISelectableColumn</c>, so the document SELECT never reads it.
/// Selectors deserialize the value from <c>data</c>.
/// </remarks>
internal sealed class DocumentDuplicatedFieldBinder<TDoc>: IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    private readonly DuplicatedField _field;
    private readonly MemberInfo[] _members;
    private readonly bool _isEnum;
    private readonly Type? _enumType;
    private readonly EnumStorage _enumStorage;

    public DocumentDuplicatedFieldBinder(DuplicatedField field, EnumStorage enumStorage)
    {
        _field = field;
        _members = field.Members;
        _enumStorage = enumStorage;

        var leafType = field.MemberType;
        _enumType = Nullable.GetUnderlyingType(leafType) is { IsEnum: true } nullableUnderlying
            ? nullableUnderlying
            : leafType.IsEnum ? leafType : null;
        _isEnum = _enumType is not null;
    }

    public string ColumnName => _field.ColumnName.ToLowerInvariant();

    public string ValueSql => "?";

    public void BindParameter(NpgsqlParameter parameter, TDoc document, IMartenSession session)
    {
        parameter.NpgsqlDbType = _field.DbType;

        object? current = document;
        foreach (var member in _members)
        {
            if (current is null) break;
            current = GetMemberValue(member, current);
        }

        if (current is null)
        {
            parameter.Value = DBNull.Value;
            return;
        }

        if (_isEnum)
        {
            parameter.Value = _enumStorage == EnumStorage.AsString
                ? current.ToString()!
                : Convert.ToInt32(current);
            return;
        }

        parameter.Value = current;
    }

    public void Apply(DbDataReader reader, int columnOrdinal, TDoc document)
    {
        // No-op — duplicated columns aren't in the document SELECT.
        // The canonical value is deserialized from the data column.
    }

    private static object? GetMemberValue(MemberInfo member, object instance)
        => member switch
        {
            PropertyInfo p => p.GetValue(instance),
            FieldInfo f => f.GetValue(instance),
            _ => null
        };
}
