#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.SqlGeneration;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing;

/// <summary>
/// #5933: translates the body of a <c>Select()</c> that follows a <c>SelectMany()</c> into a SQL expression
/// over one flattened row. The flattened row is either an element of a value collection (the <c>data</c>
/// column of the exploded rows) or a child document, whose members are located the usual way.
/// <para>
/// Only expressions whose PostgreSQL translation computes the same value as .NET are accepted: member access,
/// the element itself, integer and floating point arithmetic (no decimal division, no floating point
/// modulo), widening numeric conversions, enum to its underlying integer when enums are stored as integers,
/// string concatenation, <c>ToUpper()</c>, <c>ToLower()</c>, <c>Length</c>, and <c>ToString()</c> over
/// integers, Guids and strings. Everything else is reported back as not translatable, with the reason, so
/// the caller can refuse the query rather than silently returning something else.
/// </para>
/// </summary>
internal sealed class FlattenedRowProjection
{
    private static readonly Dictionary<Type, int> NumericRank = new()
    {
        [typeof(byte)] = 1, [typeof(sbyte)] = 1,
        [typeof(short)] = 2, [typeof(ushort)] = 2,
        [typeof(int)] = 3, [typeof(uint)] = 3,
        [typeof(long)] = 4, [typeof(ulong)] = 4,
        [typeof(float)] = 5,
        [typeof(double)] = 6,
        [typeof(decimal)] = 7
    };

    private static readonly HashSet<Type> IntegralTypes = [typeof(short), typeof(int), typeof(long)];

    private static readonly HashSet<Type> ArithmeticTypes =
        [typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal)];

    /// <summary>
    /// The scalar types a translated projection can produce, and so the types the flattened rows can be read
    /// back as.
    /// </summary>
    private static readonly HashSet<Type> ResultTypes =
    [
        typeof(string), typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal),
        typeof(bool), typeof(Guid), typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly)
    ];

    private readonly IQueryableMemberCollection _members;
    private readonly ISerializer _serializer;

    private readonly string? _tableAlias;

    private FlattenedRowProjection(IQueryableMemberCollection members, ISerializer serializer, string? tableAlias)
    {
        _members = members;
        _serializer = serializer;
        _tableAlias = tableAlias;
    }

    /// <summary>
    /// #5968: a locator is written against the table alias <c>d</c>. Inside a join, each side is read from its
    /// own CTE alias instead, the way the join's other member locators are.
    /// </summary>
    private string locate(string locator) => _tableAlias == null ? locator : locator.Replace("d.", _tableAlias + ".");

    /// <summary>
    /// Is <paramref name="type" /> a scalar that a flattened, projected row can be read back as?
    /// </summary>
    public static bool IsScalarResult(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        // #5955: an enum is read back as itself from the integer or name it is stored as (#5940)
        return ResultTypes.Contains(underlying) || underlying.IsEnum;
    }

    /// <summary>
    /// Translate a scalar projection. False, with the reason, when it cannot be translated exactly.
    /// </summary>
    public static bool TryTranslateScalar(Expression body, IQueryableMemberCollection members, ISerializer serializer,
        out ISqlFragment fragment, out string reason)
    {
        return TryTranslateScalar(body, members, serializer, null, out fragment, out reason);
    }

    /// <summary>
    /// #5968: translate a scalar over members read from the table alias <paramref name="tableAlias" /> rather than
    /// <c>d</c>, such as one side of a join.
    /// </summary>
    public static bool TryTranslateScalar(Expression body, IQueryableMemberCollection members, ISerializer serializer,
        string? tableAlias, out ISqlFragment fragment, out string reason)
    {
        fragment = null!;
        reason = string.Empty;

        if (!IsScalarResult(body.Type))
        {
            reason = $"its result type {body.Type.FullNameInCode()} is not a scalar Marten can read back";
            return false;
        }

        try
        {
            fragment = new FlattenedRowProjection(members, serializer, tableAlias).translate(body);
            return true;
        }
        catch (NotTranslatableException e)
        {
            reason = e.Message;
            return false;
        }
    }

    /// <summary>
    /// #5955: translate the key of an OrderBy()/ThenBy() over a flattened value collection or over projected rows,
    /// when the key is computed from the element -- v => -v, v => v % 3 -- rather than being the element itself.
    /// </summary>
    public static bool TryTranslateOrderingKey(Expression key, bool descending, IQueryableMemberCollection members,
        ISerializer serializer, out ISqlFragment fragment, out string reason)
    {
        return TryTranslateOrderingKey(key, descending, false, members, serializer, null, out fragment, out reason);
    }

    /// <summary>
    /// #5968: an ordering key over members read from <paramref name="tableAlias" />, lowered when the ordering
    /// ignores case.
    /// </summary>
    public static bool TryTranslateOrderingKey(Expression key, bool descending, bool ignoreCase,
        IQueryableMemberCollection members, ISerializer serializer, string? tableAlias, out ISqlFragment fragment,
        out string reason)
    {
        fragment = null!;
        if (!TryTranslateScalar(key, members, serializer, tableAlias, out var translated, out reason))
        {
            return false;
        }

        if (ignoreCase && key.Type == typeof(string))
        {
            translated = new Composite("lower(", translated, ")");
        }

        fragment = descending ? new Composite(translated, " desc") : translated;
        return true;
    }

    /// <summary>
    /// Translate an object projection -- <c>new { ... }</c>, a constructor or an object initializer -- whose
    /// every member is a translatable scalar into a <c>jsonb_build_object(...)</c>.
    /// </summary>
    public static bool TryTranslateObject(Expression body, IQueryableMemberCollection members, ISerializer serializer,
        out NewObject newObject, out string reason)
    {
        newObject = new NewObject(serializer);
        reason = string.Empty;

        try
        {
            var projection = new FlattenedRowProjection(members, serializer, null);
            switch (body)
            {
                case NewExpression { Members: not null } n:
                    for (var i = 0; i < n.Arguments.Count; i++)
                    {
                        projection.addMember(newObject, n.Members[i], n.Arguments[i]);
                    }

                    return true;

                case NewExpression n when n.Arguments.Count > 0:
                    // A positional record or DTO constructor: the JSON names are the parameter names
                    var parameters = n.Constructor!.GetParameters();
                    for (var i = 0; i < n.Arguments.Count; i++)
                    {
                        var property = n.Type.GetProperties()
                            .FirstOrDefault(p => p.Name.Equals(parameters[i].Name, StringComparison.OrdinalIgnoreCase));
                        if (property == null)
                        {
                            throw new NotTranslatableException(
                                $"constructor parameter '{parameters[i].Name}' of {n.Type.FullNameInCode()} has no matching property");
                        }

                        projection.addMember(newObject, property, n.Arguments[i]);
                    }

                    return true;

                case MemberInitExpression init when init.NewExpression.Arguments.Count == 0:
                    foreach (var binding in init.Bindings)
                    {
                        if (binding is not MemberAssignment assignment)
                        {
                            throw new NotTranslatableException($"the binding '{binding}' is not a simple assignment");
                        }

                        projection.addMember(newObject, assignment.Member, assignment.Expression);
                    }

                    return true;

                default:
                    reason = "it is not a scalar, an anonymous type, a constructor call or an object initializer";
                    return false;
            }
        }
        catch (NotTranslatableException e)
        {
            reason = e.Message;
            return false;
        }
    }

    private void addMember(NewObject newObject, MemberInfo member, Expression value)
    {
        if (!IsScalarResult(value.Type))
        {
            throw new NotTranslatableException(
                $"member '{member.Name}' is a {value.Type.FullNameInCode()}, not a scalar Marten can project");
        }

        newObject.Members[member.Name] = translate(value);
        newObject.MemberTypes[member.Name] = member.GetRawMemberType()!;
    }

    private ISqlFragment translate(Expression node)
    {
        if (ParameterFinder.FindIn(node) == null)
        {
            return constant(node);
        }

        switch (node)
        {
            case ParameterExpression:
                return element();

            case MemberExpression member:
                return translateMember(member);

            case UnaryExpression unary:
                return translateUnary(unary);

            case BinaryExpression binary:
                return translateBinary(binary);

            case MethodCallExpression call:
                return translateMethod(call);
        }

        throw new NotTranslatableException($"'{node}' has no SQL translation");
    }

    private ISqlFragment constant(Expression node)
    {
        var value = node.ReduceToConstant().Value;
        if (value == null)
        {
            throw new NotTranslatableException($"'{node}' is null");
        }

        var type = value.GetType();
        if (type != typeof(string) && type != typeof(Guid) && !ArithmeticTypes.Contains(type))
        {
            throw new NotTranslatableException($"the constant '{node}' is a {type.FullNameInCode()}");
        }

        return new CommandParameter(value);
    }

    /// <summary>
    /// The whole flattened row. Only a value collection's element is a scalar; a whole child document is not.
    /// </summary>
    private ISqlFragment element()
    {
        if (_members is not IValueCollectionMember)
        {
            throw new NotTranslatableException("the whole child document is not a scalar");
        }

        var elementType = Nullable.GetUnderlyingType(_members.ElementType) ?? _members.ElementType;

        return elementType == typeof(string)
            ? new LiteralSql(locate("d.data"))
            : new LiteralSql(locate($"CAST(d.data as {pgType(elementType)})"));
    }

    private ISqlFragment translateMember(MemberExpression node)
    {
        if (node.Member.Name == nameof(string.Length) && node.Expression?.Type == typeof(string))
        {
            return new Composite("length(", translate(node.Expression), ")");
        }

        if (_members is IValueCollectionMember || !isMemberChainOfTheRow(node))
        {
            throw new NotTranslatableException($"'{node}' is not a member Marten can locate");
        }

        if (!IsScalarResult(node.Type))
        {
            throw new NotTranslatableException($"'{node}' is a {node.Type.FullNameInCode()}, not a scalar");
        }

        var queryable = _members.MemberFor(node);
        return new LiteralSql(locate(queryable.TypedLocator));
    }

    private static bool isMemberChainOfTheRow(MemberExpression node)
    {
        Expression? current = node;
        while (current is MemberExpression m)
        {
            current = m.Expression;
        }

        return current is ParameterExpression;
    }

    private ISqlFragment translateUnary(UnaryExpression node)
    {
        switch (node.NodeType)
        {
            case ExpressionType.Negate or ExpressionType.NegateChecked
                when ArithmeticTypes.Contains(underlying(node.Type)):
                return new Composite("(-", translate(node.Operand), ")");

            case ExpressionType.Convert or ExpressionType.ConvertChecked:
                return translateConversion(node);
        }

        throw new NotTranslatableException($"'{node}' has no SQL translation");
    }

    private ISqlFragment translateConversion(UnaryExpression node)
    {
        var source = underlying(node.Operand.Type);
        var target = underlying(node.Type);

        // Identity, or wrapping into / unwrapping from Nullable<T>
        if (source == target)
        {
            return translate(node.Operand);
        }

        if (source.IsEnum)
        {
            if (_serializer.EnumStorage != EnumStorage.AsInteger || target != Enum.GetUnderlyingType(source))
            {
                throw new NotTranslatableException(
                    $"'{node}' converts an enum, which is only translated to its underlying integer when enums are stored as integers");
            }

            return enumAsInteger(node.Operand);
        }

        if (isWidening(source, target))
        {
            return new Composite("CAST(", translate(node.Operand), $" as {pgType(target)})");
        }

        throw new NotTranslatableException(
            $"'{node}' is a narrowing or lossy conversion, which PostgreSQL does not compute the way .NET does");
    }

    private ISqlFragment enumAsInteger(Expression operand)
    {
        if (operand is ParameterExpression)
        {
            // The exploded element, already stored and cast as an integer
            return element();
        }

        if (operand is MemberExpression member && _members is not IValueCollectionMember &&
            isMemberChainOfTheRow(member))
        {
            var queryable = _members.MemberFor(member);
            return new LiteralSql(locate($"CAST({queryable.RawLocator} as integer)"));
        }

        throw new NotTranslatableException($"'{operand}' is not an enum Marten can locate");
    }

    private static bool isWidening(Type source, Type target)
    {
        if (!NumericRank.TryGetValue(source, out var sourceRank) || !NumericRank.TryGetValue(target, out var targetRank))
        {
            return false;
        }

        const int integerCeiling = 4;
        if (sourceRank <= integerCeiling)
        {
            return targetRank > sourceRank;
        }

        return source == typeof(float) && target == typeof(double);
    }

    private ISqlFragment translateBinary(BinaryExpression node)
    {
        if (node.NodeType == ExpressionType.Add && node.Type == typeof(string))
        {
            return new Composite("concat(", text(node.Left), ", ", text(node.Right), ")");
        }

        var type = underlying(node.Left.Type);
        if (!ArithmeticTypes.Contains(type) || underlying(node.Right.Type) != type)
        {
            throw new NotTranslatableException($"'{node}' is not arithmetic over numbers of one type");
        }

        var op = node.NodeType switch
        {
            ExpressionType.Add or ExpressionType.AddChecked => "+",
            ExpressionType.Subtract or ExpressionType.SubtractChecked => "-",
            ExpressionType.Multiply or ExpressionType.MultiplyChecked => "*",
            ExpressionType.Divide when type != typeof(decimal) => "/",
            ExpressionType.Modulo when IntegralTypes.Contains(type) => "%",
            _ => throw new NotTranslatableException(
                $"'{node}' ({node.NodeType} over {type.Name}) is not computed by PostgreSQL the way .NET computes it")
        };

        return new Composite("(", translate(node.Left), $" {op} ", translate(node.Right), ")");
    }

    /// <summary>
    /// One operand of a string concatenation. A non-string operand reaches string.Concat boxed to object.
    /// </summary>
    private ISqlFragment text(Expression node)
    {
        if (node is UnaryExpression { NodeType: ExpressionType.Convert } convert && node.Type == typeof(object))
        {
            node = convert.Operand;
        }

        if (node.Type == typeof(string))
        {
            return translate(node);
        }

        return asText(node);
    }

    /// <summary>
    /// ToString() of an integer or a Guid, whose PostgreSQL text form is the .NET one.
    /// </summary>
    private ISqlFragment asText(Expression node)
    {
        var type = underlying(node.Type);
        if (type == typeof(string))
        {
            return translate(node);
        }

        if (IntegralTypes.Contains(type) || type == typeof(Guid))
        {
            return new Composite("CAST(", translate(node), " as text)");
        }

        throw new NotTranslatableException(
            $"'{node}' is a {node.Type.FullNameInCode()}, whose text form PostgreSQL does not write the way .NET does");
    }

    private ISqlFragment translateMethod(MethodCallExpression node)
    {
        if (node.Method.DeclaringType == typeof(string) && node.Method.Name == nameof(string.Concat) &&
            node.Object == null && node.Arguments.Count is >= 2 and <= 4)
        {
            var parts = new List<object> { "concat(" };
            for (var i = 0; i < node.Arguments.Count; i++)
            {
                if (i > 0) parts.Add(", ");
                parts.Add(text(node.Arguments[i]));
            }

            parts.Add(")");
            return new Composite(parts.ToArray());
        }

        if (node.Object != null && node.Arguments.Count == 0)
        {
            if (node.Object.Type == typeof(string))
            {
                switch (node.Method.Name)
                {
                    case nameof(string.ToUpper) or nameof(string.ToUpperInvariant):
                        return new Composite("upper(", translate(node.Object), ")");
                    case nameof(string.ToLower) or nameof(string.ToLowerInvariant):
                        return new Composite("lower(", translate(node.Object), ")");
                }
            }

            if (node.Method.Name == nameof(ToString))
            {
                return asText(node.Object);
            }
        }

        throw new NotTranslatableException($"the method call '{node}' has no SQL translation");
    }

    private string pgType(Type type)
    {
        return PostgresqlProvider.Instance.GetDatabaseType(type, _serializer.EnumStorage);
    }

    private static Type underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    /// <summary>
    /// SQL text and fragments, written in order.
    /// </summary>
    private sealed class Composite: ISqlFragment
    {
        private readonly object[] _parts;

        public Composite(params object[] parts)
        {
            _parts = parts;
        }

        public void Apply(ICommandBuilder builder)
        {
            foreach (var part in _parts)
            {
                if (part is ISqlFragment fragment)
                {
                    fragment.Apply(builder);
                }
                else
                {
                    builder.Append((string)part);
                }
            }
        }
    }

    // Internal to the translation and always caught by it. It derives from MartenException like every other
    // exception Marten defines.
    private sealed class NotTranslatableException: MartenException
    {
        public NotTranslatableException(string message): base(message)
        {
        }
    }
}
