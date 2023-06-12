using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;
using JasperFx.Core.Reflection;
using Marten.Linq.Filters;
using Marten.Linq.Parsing;
using Marten.Util;
using Newtonsoft.Json;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Fields;

public abstract class FieldBase: IField
{
    protected FieldBase(string dataLocator, string pgType, Casing casing, MemberInfo[] members)
    {
        if (members == null || members.Length == 0)
        {
            throw new ArgumentNullException(nameof(members));
        }

        Members = members;

        lastMember = members.Last();

        FieldType = lastMember.GetMemberType();

        var locator = dataLocator;

        for (var i = 0; i < members.Length - 1; i++)
        {
            var member = members[i];
            var memberLocator = determineMemberLocator(casing, member);

            locator += $" -> '{memberLocator}'";
        }

        parentLocator = locator;
        lastMemberName = determineMemberLocator(casing, lastMember);

        RawLocator = TypedLocator = $"{parentLocator} ->> '{lastMemberName}'";

        PgType = pgType;

        JSONBLocator = $"CAST({RawLocator} as jsonb)";
    }

    private static string determineMemberLocator(Casing casing, MemberInfo member)
    {
        var memberLocator = member.Name.FormatCase(casing);
        if (member.TryGetAttribute<JsonPropertyAttribute>(out var newtonsoftAtt) && newtonsoftAtt.PropertyName is not null)
        {
            memberLocator = newtonsoftAtt.PropertyName;
        }

        if (member.TryGetAttribute<JsonPropertyNameAttribute>(out var stjAtt))
        {
            memberLocator = stjAtt.Name;
        }

        return memberLocator;
    }

    protected string lastMemberName { get; }

    protected string parentLocator { get; }

    protected MemberInfo lastMember { get; }

    public string PgType { get; set; } // settable so it can be overidden by users

    public virtual ISqlFragment CreateComparison(string op, ConstantExpression value, Expression memberExpression)
    {
        if (value.Value == null)
        {
            return op == "=" ? new IsNullFilter(this) : new IsNotNullFilter(this);
        }

        var def = new CommandParameter(value);
        return new ComparisonFilter(this, def, op);
    }

    [Obsolete("Try to eliminate this")]
    public bool ShouldUseContainmentOperator()
    {
        return PostgresqlProvider.Instance.ContainmentOperatorTypes.Contains(FieldType);
    }

    public abstract string SelectorForDuplication(string pgType);

    public virtual string ToOrderExpression(Expression expression)
    {
        return TypedLocator;
    }

    public virtual string LocatorForIncludedDocumentId => TypedLocator;

    /// <summary>
    ///     Locate the data for this field as JSONB
    /// </summary>
    public string JSONBLocator { get; set; }

    public Type FieldType { get; }

    public MemberInfo[] Members { get; }

    public string RawLocator { get; protected set; }

    public string TypedLocator { get; protected set; }

    public virtual object GetValueForCompiledQueryParameter(Expression valueExpression)
    {
        return valueExpression.Value();
    }

    public string LocatorFor(string rootTableAlias)
    {
        // Super hokey.
        return TypedLocator.Replace("d.", rootTableAlias + ".");
    }

    void ISqlFragment.Apply(CommandBuilder builder)
    {
        builder.Append(TypedLocator);
    }

    bool ISqlFragment.Contains(string sqlText)
    {
        return TypedLocator.Contains(sqlText);
    }
}
