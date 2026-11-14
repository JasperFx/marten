#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Exceptions;
using Marten.Linq.Parsing;
using Marten.Linq.Parsing.Operators;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Services;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using Weasel.Storage;

namespace Marten.Linq.Members.Unions;

/// <summary>
///     #5735: recognising the LINQ shapes over a C# union-typed document member (.NET 11).
/// </summary>
/// <remarks>
///     <para>
///         The compiler refuses a union pattern in an expression tree (<c>x.Payment is Card</c> is CS8122),
///         so a query reaches the active case through <c>IUnion.Value</c>:
///     </para>
///     <list type="bullet">
///         <item><c>x.Payment.Value is Card</c>, a type test</item>
///         <item><c>((Card)x.Payment.Value).Last4</c> or <c>(x.Payment.Value as Card).Last4</c>, a case member</item>
///         <item><c>(int)x.Code.Value</c>, a primitive case read as itself</item>
///         <item><c>x.Payment.Value == null</c>, a default union</item>
///     </list>
///     <para>
///         System.Text.Json writes the bare case at the union member's own path, so <c>Value</c> is not a key in
///         the stored JSON. A member chain records a cast of <c>Value</c> to a case as a <see cref="UnionCaseCast" />,
///         and <see cref="UnionValueMember" /> refuses any member reached without one, so a visitor that drops the
///         cast fails loudly instead of reading a property of whichever case happens to be stored.
///     </para>
///     <para>
///         Before .NET 11 <see cref="UnionTypes.IsUnion" /> is false for every type, so none of this is reached.
///     </para>
/// </remarks>
internal static class UnionMembers
{
    /// <summary>
    ///     <c>x.Payment.Value</c> where <c>Payment</c> is a union. Never true before .NET 11.
    /// </summary>
    public static bool IsUnionValue(Expression? expression)
        => expression is MemberExpression { Member.Name: "Value", Expression: { } owner } && owner.Type.IsUnion();

    /// <summary>
    ///     A cast of a union's <c>Value</c> to a case, <c>(Card)x.Payment.Value</c> or
    ///     <c>x.Payment.Value as Card</c>, as the marker a member chain carries. Null for anything else,
    ///     including a cast to <c>object</c>, which changes nothing.
    /// </summary>
    public static UnionCaseCast? CaseCastFor(UnaryExpression node)
    {
        if (node.NodeType is not (ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs))
        {
            return null;
        }

        if (node.Type == typeof(object) || !IsUnionValue(node.Operand)) return null;

        var unionType = ((MemberExpression)node.Operand).Expression!.Type;
        return new UnionCaseCast(unionType, node.Type, node.NodeType == ExpressionType.TypeAs, node.ToString());
    }

    public static BadLinqExpressionException RefuseComparison(string expression, Type unionType)
        => new($"Marten cannot translate '{expression}' to SQL: a union ({unionType.ShortNameInCode()}) is "
               + "only compared to null. Test its case (x.Member.Value is Case), compare a member of the case "
               + "(((Case)x.Member.Value).Name == \"...\"), or cast a primitive case to compare it "
               + "((int)x.Member.Value > 3).");

    public static BadLinqExpressionException RefuseKey(string expression, string position)
        => new($"Marten cannot translate '{expression}' {position}: a union is stored as its active case, "
               + "whichever that is, so it has no one value SQL can sort or group by. Use a member of a case "
               + "(((Case)x.Member.Value).Name), or a primitive case cast to its type.");

    /// <summary>
    ///     A union used as a GroupBy() key, refused: it has no one value to group by.
    /// </summary>
    public static void AssertNotGroupingKey(IQueryableMember member, Expression expression)
    {
        if (member is UnionMember or UnionValueMember)
        {
            throw RefuseKey(expression.ToString(), "as a GroupBy() key");
        }
    }

    /// <summary>
    ///     A comparison against a member of a case only matches rows holding that case. The member's locator is
    ///     already NULL on any other case, but <c>is null</c> would then be true for every other case's row, so
    ///     the type test is ANDed in. The one exception is <c>(x.Payment.Value as Card) == null</c>, where the
    ///     case itself is compared: that is true exactly when the union does not hold a Card, as in C#.
    /// </summary>
    public static ISqlFragment GuardComparison(ISqlFragment fragment, IQueryableMember? left, IQueryableMember? right)
    {
        var guards = new List<string>();
        collectGuards(left, guards);
        collectGuards(right, guards);

        if (guards.Count == 0) return fragment;

        var compound = CompoundWhereFragment.For("and");
        foreach (var guard in guards.Distinct()) compound.Register(new WhereFragment(guard));
        compound.Register(fragment);
        return compound;
    }

    private static void collectGuards(IQueryableMember? member, List<string> guards)
    {
        if (member == null) return;

        var ancestors = member.Ancestors;
        for (var i = 0; i < ancestors.Length; i++)
        {
            if (ancestors[i] is not UnionCaseScope scope) continue;

            // The case itself, reached through `as`: a null there means "not this case".
            if (i == ancestors.Length - 1 && scope.IsTypeAs) continue;

            guards.Add(scope.Guard);
        }
    }
}

/// <summary>
///     The marker a member chain carries for a cast of a union's <c>Value</c> to one of its cases, as
///     <see cref="Marten.Linq.Members.ValueCollections.ArrayIndexMember" /> marks an indexer.
/// </summary>
internal sealed class UnionCaseCast: MemberInfo
{
    public UnionCaseCast(Type unionType, Type caseType, bool isTypeAs, string expression)
    {
        DeclaringType = unionType;
        CaseType = caseType;
        IsTypeAs = isTypeAs;
        Expression = expression;
    }

    /// <summary>The type the union's Value is cast to.</summary>
    public Type CaseType { get; }

    /// <summary>True for <c>x.Payment.Value as Card</c>, false for <c>(Card)x.Payment.Value</c>.</summary>
    public bool IsTypeAs { get; }

    /// <summary>The cast as written, for messages.</summary>
    public string Expression { get; }

    public override Type DeclaringType { get; }
    public override MemberTypes MemberType => MemberTypes.Custom;
    public override string Name => $"({CaseType.ShortNameInCode()})";
    public override Type ReflectedType => CaseType;

    public override object[] GetCustomAttributes(bool inherit) => [];
    public override object[] GetCustomAttributes(Type attributeType, bool inherit) => [];
    public override bool IsDefined(Type attributeType, bool inherit) => false;
}

/// <summary>
///     A union-typed document member, <c>x.Payment</c>: located as <c>jsonb</c>, compared only to null, and the
///     owner of the type tests and case members reached through its <c>Value</c>.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
    Justification = "Reads JsonTypeInfo for union case types from the store's own serializer options; document types and their members are preserved at registration per the AOT publishing guide.")]
[UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
    Justification = "Reads JsonTypeInfo for union case types from the store's own serializer options; document types and their members are preserved at registration per the AOT publishing guide.")]
internal sealed class UnionMember: QueryableMember, IComparableMember
{
    private readonly StoreOptions _options;
    private readonly SystemTextJsonSerializer _serializer;
    private readonly Dictionary<(Type, bool), IQueryableMember> _cases = new();
    private UnionShape? _shape;
    private UnionValueMember? _value;

    private UnionMember(StoreOptions options, SystemTextJsonSerializer serializer, IQueryableMember parent,
        MemberInfo member, Type unionType): base(parent, serializer.Casing, member)
    {
        _options = options;
        _serializer = serializer;
        Parent = parent;
        UnionType = unionType;

        RawLocator = TypedLocator = $"{parent.RawLocator} -> '{MemberName}'";
        JSONBLocator = RawLocator;
        NullTestLocator = $"{parent.RawLocator} ->> '{MemberName}'";
    }

    public static UnionMember Create(StoreOptions options, IQueryableMember parent, MemberInfo member, Type unionType)
    {
        if (options.Serializer() is not SystemTextJsonSerializer serializer)
        {
            throw new BadLinqExpressionException(
                $"Marten cannot query the union member {member.DeclaringType?.ShortNameInCode()}.{member.Name} "
                + $"({unionType.ShortNameInCode()}) with the {options.Serializer().GetType().ShortNameInCode()}: "
                + "only System.Text.Json writes a C# union as its bare active case, which is what the SQL for a "
                + "union reads. Use the System.Text.Json serializer to query union members.");
        }

        if (parent is RootMember || parent.Ancestors.Any(x => x is RootMember))
        {
            throw new BadLinqExpressionException(
                $"Marten cannot translate the union member {member.DeclaringType?.ShortNameInCode()}.{member.Name} "
                + "inside a child collection predicate, which Marten may render as JSONPath or containment where "
                + "the union's case cannot be tested. Query the union on the document itself, or materialize the "
                + "query and test the case in memory.");
        }

        return new UnionMember(options, serializer, parent, member, unionType);
    }

    /// <summary>The union's parent: the document, or the child document the union is a member of.</summary>
    public IQueryableMember Parent { get; }

    /// <summary>The union type, never Nullable.</summary>
    public Type UnionType { get; }

    private UnionShape Shape => _shape ??= UnionShape.For(UnionType, _serializer.WriteOptions,
        _serializer.EnumStorage == Weasel.Core.EnumStorage.AsString);

    public override IQueryableMember FindMember(MemberInfo member)
    {
        if (member.Name == "Value" && member.DeclaringType is { IsGenericType: true } declaring &&
            declaring.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            // x.Maybe.Value of a Nullable<TUnion>: the same JSON
            return this;
        }

        if (member.Name == "HasValue")
        {
            return base.FindMember(member);
        }

        if (member.Name == "Value" && member.DeclaringType != null && member.DeclaringType.IsAssignableFrom(UnionType))
        {
            return _value ??= new UnionValueMember(this);
        }

        throw new BadLinqExpressionException(
            $"Marten cannot translate the member {UnionType.ShortNameInCode()}.{member.Name}: reach the union's "
            + "active case through Value, as x.Member.Value is Case or ((Case)x.Member.Value).Name.");
    }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (constant.Value() == null)
        {
            return op == "=" ? new IsNullFilter(this) : new IsNotNullFilter(this);
        }

        throw UnionMembers.RefuseComparison($"{MemberName} {op} {constant}", UnionType);
    }

    public override string BuildOrderingExpression(Ordering ordering, CasingRule casingRule)
        => throw UnionMembers.RefuseKey(ordering.Expression?.ToString() ?? MemberName, "in an ordering");

    public override string SelectorForDuplication(string pgType)
        => throw new BadLinqExpressionException(
            $"Marten cannot duplicate the union member {MemberName} into a column: it is stored as its active case, "
            + "whichever that is.");

    /// <summary>
    ///     The SQL predicate for <c>x.Member.Value is <paramref name="tested" /></c>: true when the stored
    ///     union holds a case assignable to <paramref name="tested" />, false (never NULL) otherwise.
    /// </summary>
    public string TypeTest(Type tested, string expression)
    {
        var cases = Shape.CasesMatching(tested);
        if (cases.Count == 0)
        {
            throw new BadLinqExpressionException(
                $"Marten cannot translate '{expression}' to SQL: {tested.ShortNameInCode()} is not a case of "
                + $"{UnionType.ShortNameInCode()}, whose cases are "
                + $"{Shape.CaseTypes.Select(x => x.ShortNameInCode()).Join(", ")}.");
        }

        var tests = cases
            .Select(x => PostgresqlUnionSqlDialect.Instance.TypeTest(Parent.RawLocator, [MemberName],
                Shape.TestFor(x, expression)))
            .ToArray();

        var predicate = tests.Length == 1 ? tests[0] : "(" + tests.Join(" or ") + ")";

        // The dialect's predicate is NULL when the member is missing; NOT(NULL) would drop the row from
        // `!(x.Payment.Value is Card)`, where C# says true.
        return $"coalesce({predicate}, false)";
    }

    /// <summary>
    ///     The case <paramref name="cast" /> names, as a member every locator of which is NULL unless the union
    ///     holds that case.
    /// </summary>
    public IQueryableMember CaseMember(UnionCaseCast cast)
    {
        if (_cases.TryGetValue((cast.CaseType, cast.IsTypeAs), out var existing)) return existing;

        var caseType = Nullable.GetUnderlyingType(cast.CaseType) ?? cast.CaseType;
        var scope = new UnionCaseScope(Parent, TypeTest(caseType, cast.Expression), cast.IsTypeAs);

        IQueryableMember member;
        if (caseType == typeof(string) || caseType.IsValueTypeForQuerying())
        {
            if (caseType.IsEnum)
            {
                throw new BadLinqExpressionException(
                    $"Marten cannot translate '{cast.Expression}' to SQL: reading an enum case of a union is not "
                    + "supported. Test the case instead (x.Member.Value is " + caseType.ShortNameInCode() + ").");
            }

            // A primitive case is read as itself, at the union member's own path.
            member = _options.CreateQueryableMember(Member, scope, caseType);
            if (member is QueryableMember queryable) queryable.MemberType = cast.CaseType;
        }
        else
        {
            member = new UnionCaseDocument(_options, scope, _serializer.Casing, Member, caseType);
        }

        _cases[(cast.CaseType, cast.IsTypeAs)] = member;
        return member;
    }
}

/// <summary>
///     A union member's <c>Value</c>: compared to null, tested for its case, or cast to a case to read it. Any
///     other member read off it is refused, because the chain lost the cast that names the case.
/// </summary>
internal sealed class UnionValueMember: IQueryableMember, IHasChildrenMembers, IComparableMember
{
    public UnionValueMember(UnionMember union)
    {
        Union = union;
    }

    public UnionMember Union { get; }

    public Type MemberType => typeof(object);
    public string JsonPathSegment => Union.JsonPathSegment;
    public string MemberName => Union.MemberName;
    public string TypedLocator => Union.TypedLocator;
    public string RawLocator => Union.RawLocator;
    public string JSONBLocator => Union.JSONBLocator;
    public IQueryableMember[] Ancestors => Union.Ancestors;
    public string LocatorForIncludedDocumentId => throw refuseValue();
    public string NullTestLocator => Union.NullTestLocator;

    public void Apply(ICommandBuilder builder) => builder.Append(TypedLocator);

    public IQueryableMember FindMember(MemberInfo member)
    {
        if (member is UnionCaseCast cast) return Union.CaseMember(cast);

        throw refuseValue(member.Name);
    }

    public void ReplaceMember(MemberInfo member, IQueryableMember queryableMember)
        => throw new NotSupportedException();

    public ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (constant.Value() == null)
        {
            return op == "=" ? new IsNullFilter(Union) : new IsNotNullFilter(Union);
        }

        throw UnionMembers.RefuseComparison($"{MemberName}.Value {op} {constant}", Union.UnionType);
    }

    public string BuildOrderingExpression(Ordering ordering, CasingRule casingRule)
        => throw UnionMembers.RefuseKey(ordering.Expression?.ToString() ?? MemberName + ".Value", "in an ordering");

    public Dictionary<string, object> FindOrPlaceChildDictionaryForContainment(Dictionary<string, object> dict)
        => throw refuseValue();

    public void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict, ConstantExpression constant)
        => throw refuseValue();

    public string SelectorForDuplication(string pgType) => throw refuseValue();

    private BadLinqExpressionException refuseValue(string? member = null)
        => new($"Marten cannot translate {MemberName}.Value{(member == null ? "" : "." + member)}: cast a union's "
               + "Value to one of its cases to read it, as ((Case)x.Member.Value).Name or "
               + "(x.Member.Value as Case).Name, or test it with x.Member.Value is Case.");
}

/// <summary>
///     The stand-in for a union's parent while reading one case: every locator is the parent's own, NULL unless
///     the union holds the case. A case member built on top of it (<see cref="UnionCaseDocument" />, or a primitive
///     case's own scalar member) derives each locator from this one, so every member of the case is guarded
///     without knowing it.
/// </summary>
internal sealed class UnionCaseScope: IQueryableMember
{
    private readonly IQueryableMember _parent;

    public UnionCaseScope(IQueryableMember parent, string guard, bool isTypeAs)
    {
        _parent = parent;
        Guard = guard;
        IsTypeAs = isTypeAs;
        RawLocator = $"(CASE WHEN {guard} THEN {parent.RawLocator} END)";
    }

    /// <summary>The type test for the case.</summary>
    public string Guard { get; }

    /// <summary>The case was reached through <c>as</c>, where a null means "not this case".</summary>
    public bool IsTypeAs { get; }

    public Type MemberType => _parent.MemberType;
    public string JsonPathSegment => _parent.JsonPathSegment;
    public string MemberName => _parent.MemberName;
    public string RawLocator { get; }
    public string TypedLocator => RawLocator;
    public string JSONBLocator => RawLocator;
    public string NullTestLocator => RawLocator;

    // Stands in for the parent, so a case member's ancestry reads [..., scope, case].
    public IQueryableMember[] Ancestors => _parent.Ancestors;

    public string LocatorForIncludedDocumentId => throw refuse();

    public void Apply(ICommandBuilder builder) => builder.Append(RawLocator);

    public string BuildOrderingExpression(Ordering ordering, CasingRule casingRule) => RawLocator;

    public Dictionary<string, object> FindOrPlaceChildDictionaryForContainment(Dictionary<string, object> dict)
        => throw refuse();

    public void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict, ConstantExpression constant)
        => throw refuse();

    public string SelectorForDuplication(string pgType) => throw refuse();

    private static BadLinqExpressionException refuse()
        => new("Marten cannot use a member of a union case here: containment, includes and duplicated columns "
               + "do not know which case the union holds.");
}

/// <summary>
///     An object case of a union, <c>(Card)x.Payment.Value</c>: a child document located through a
///     <see cref="UnionCaseScope" />, so it and all its members are NULL unless the union holds the case.
/// </summary>
internal sealed class UnionCaseDocument: ChildDocument
{
    public UnionCaseDocument(StoreOptions options, UnionCaseScope scope, Casing casing, MemberInfo unionMember,
        Type caseType): base(options, scope, casing, unionMember)
    {
        MemberType = caseType;
    }
}
