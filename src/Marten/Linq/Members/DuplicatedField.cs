#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Parsing;
using Marten.Linq.Parsing.Operators;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Util;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using Weasel.Postgresql.Tables;

namespace Marten.Linq.Members;

public class DuplicatedField: IQueryableMember, IComparableMember, IHasChildrenMembers
{
    private readonly Func<object, object> _parseObject = o => o;
    private readonly bool useTimestampWithoutTimeZoneForDateTime;
    private string _columnName;

    public DuplicatedField(EnumStorage enumStorage, QueryableMember innerMember, ValueTypeInfo? valueTypeInfo,
        bool useTimestampWithoutTimeZoneForDateTime = true, bool notNull = false)
    {
        InnerMember = innerMember;
        MemberName = InnerMember.Ancestors.OfType<QueryableMember>().Select(x => x.MemberName).Append(InnerMember.MemberName).Join("");

        Members = InnerMember.Ancestors.OfType<QueryableMember>().Append(InnerMember).Select(x => x.Member).ToArray();

        ValueTypeInfo = valueTypeInfo;

        NotNull = notNull;
        ColumnName = MemberName.ToTableAlias();
        this.useTimestampWithoutTimeZoneForDateTime = useTimestampWithoutTimeZoneForDateTime;

        PgType = PostgresqlProvider.Instance.GetDatabaseType(MemberType, enumStorage);

        if (MemberType.IsEnum || MemberType.IsNullable() && MemberType.GetGenericArguments()[0].IsEnum)
        {
            var enumType = MemberType.IsEnum ? MemberType : MemberType.GetGenericArguments()[0];

            if (enumStorage == EnumStorage.AsString)
            {
                DbType = NpgsqlDbType.Varchar;
                PgType = "varchar";

                _parseObject = raw =>
                {
                    if (raw == null)
                    {
                        return null;
                    }

                    return Enum.GetName(enumType, raw);
                };
            }
            else
            {
                DbType = NpgsqlDbType.Integer;
                PgType = "integer";
                _parseObject = raw =>
                {
                    if (raw == null)
                    {
                        return null;
                    }

                    return (int)raw;
                };
            }
        }
        else if (MemberType.IsDateTime())
        {
            PgType = this.useTimestampWithoutTimeZoneForDateTime
                ? "timestamp without time zone"
                : "timestamp with time zone";
            DbType = this.useTimestampWithoutTimeZoneForDateTime
                ? NpgsqlDbType.Timestamp
                : NpgsqlDbType.TimestampTz;
        }
        else if (MemberType == typeof(DateTimeOffset) || MemberType == typeof(DateTimeOffset?))
        {
            PgType = "timestamp with time zone";
            DbType = NpgsqlDbType.TimestampTz;
        }
        else
        {
            DbType = PostgresqlProvider.Instance.ToParameterType(MemberType);
        }
    }

    public MemberInfo[] Members { get;  }

    public string JsonPathSegment => throw new NotSupportedException();
    public string BuildOrderingExpression(Ordering ordering, CasingRule casingRule)
    {
        if (ordering.Direction == OrderingDirection.Desc) return $"{TypedLocator} desc";

        return TypedLocator;
    }

    public IQueryableMember[] Ancestors => InnerMember.Ancestors;
    public Dictionary<string, object> FindOrPlaceChildDictionaryForContainment(Dictionary<string, object> dict)
    {
        throw new NotSupportedException();
    }

    public void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict, ConstantExpression constant)
    {
        throw new NotSupportedException();
    }

    public string NullTestLocator => RawLocator;

    public bool NotNull { get; internal set; }

    public bool OnlyForSearching { get; set; } = false;

    /// <summary>
    ///     Used to override the assigned DbType used by Npgsql when a parameter
    ///     is used in a query against this column
    /// </summary>
    public NpgsqlDbType DbType { get; set; }

    public ValueTypeInfo? ValueTypeInfo { get; }

    internal UpsertArgument UpsertArgument
    {
        get
        {
            UpsertArgument upsertArgument = new()
            {
                Arg = "arg_" + ColumnName.ToLower()
                , Column = ColumnName.ToLower()
                , PostgresType = PgType
                , Members = Members
                , DbType = DbType
            };

            if (!IsInnerMemberValueType()) return upsertArgument;

            if (InnerMember.Member.GetRawMemberType()!.IsNullable())
            {
                upsertArgument.ParameterValue = $"{InnerMember.Member.Name}.Value.{ValueTypeInfo!.ValueProperty.Name}";
            }

            upsertArgument.ParameterValue = $"{InnerMember.Member.Name}.{ValueTypeInfo!.ValueProperty.Name}";

            return upsertArgument;
        }
    }

    public string ColumnName
    {
        get => _columnName;
        set
        {
            _columnName = value;
            TypedLocator = "d." + _columnName;
        }
    }

    internal QueryableMember InnerMember { get; }
    public string MemberName { get; }

    public string PgType { get; set; } // settable so it can be overidden by users

    public string RawLocator => TypedLocator;


    public object GetValueForCompiledQueryParameter(Expression valueExpression)
    {
        var value = valueExpression.Value();
        return _parseObject(value);
    }

    string IQueryableMember.SelectorForDuplication(string pgType)
    {
        throw new NotSupportedException();
    }

    public ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (IsInnerMemberValueType())
        {
            return InnerMember.CreateComparison(op, constant);
        }

        if (constant.Value == null)
        {
            return op switch
            {
                "=" => new IsNullFilter(this),
                "!=" => new IsNotNullFilter(this),
                _ => throw new BadLinqExpressionException(
                    $"Can only compare property {MemberName} by '=' or '!=' with null value")
            };
        }

        return new ComparisonFilter(this, new CommandParameter(_parseObject(constant.Value), DbType), op);
    }

    public string JSONBLocator { get; set; }
    public string LocatorForIncludedDocumentId => TypedLocator;

    public string TypedLocator { get; set; }

    void ISqlFragment.Apply(ICommandBuilder builder)
    {
        builder.Append(TypedLocator);
    }

    public Type MemberType => InnerMember.MemberType;

    public string UpdateSqlFragment()
    {
        return $"\"{ColumnName.ToLowerInvariant()}\" = {InnerMember.SelectorForDuplication(PgType)}";
    }

    public static DuplicatedField For<T>(StoreOptions options, Expression<Func<T, object>> expression,
        bool useTimestampWithoutTimeZoneForDateTime = true)
    {
        var inner = new DocumentMapping<T>(options).QueryMembers.MemberFor(expression);

        return new DuplicatedField(options.EnumStorage, (QueryableMember)inner, null, useTimestampWithoutTimeZoneForDateTime);
    }

    // I say you don't need a ForeignKey
    public virtual TableColumn ToColumn()
    {
        return new TableColumn(ColumnName, PgType);
    }


    public IQueryableMember FindMember(MemberInfo member)
    {
        if (member.Name == "Value") return this;

        // Only really using this for string ToLower() and ToUpper()
        if (MemberType == typeof(string))
        {
            return member.Name switch
            {
                nameof(string.ToLower) => new StringMember(this, Casing.Default, member)
                {
                    RawLocator = $"lower({RawLocator})", TypedLocator = $"lower({RawLocator})"
                },
                nameof(string.ToUpper) => new StringMember(this, Casing.Default, member)
                {
                    RawLocator = $"upper({RawLocator})", TypedLocator = $"upper({RawLocator})"
                },
                nameof(string.ToLowerInvariant) => new StringMember(this, Casing.Default, member)
                {
                    RawLocator = $"lower({RawLocator})", TypedLocator = $"lower({RawLocator})"
                },
                nameof(string.ToUpperInvariant) => new StringMember(this, Casing.Default, member)
                {
                    RawLocator = $"upper({RawLocator})", TypedLocator = $"upper({RawLocator})"
                },
                _ => throw new BadLinqExpressionException($"Marten does not (yet) support the method {member.Name} in duplicated field querying")
            };
        }

        throw new BadLinqExpressionException(
            $"Marten does not (yet) support the method {member.Name} in duplicated field querying");
    }

    public void ReplaceMember(MemberInfo member, IQueryableMember queryableMember)
    {
        // Nothing
    }

    private bool IsInnerMemberValueType() => ValueTypeInfo is not null;
}
