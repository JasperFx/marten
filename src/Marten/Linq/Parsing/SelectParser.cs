#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.SqlGeneration;
using Marten.Util;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Linq.Parsing;

/// <summary>
/// Signals that a Select() projection is not a "simple" flat member-access transform
/// (GH-5011) -- e.g. it contains a method call, arithmetic, a cast/conversion, or a
/// conditional expression -- and cannot be translated to a server-side
/// <c>jsonb_build_object(...)</c> expression. Callers catch this and fall back to
/// compiling the original Select() lambda and applying it against the fully
/// deserialized document on the client, so behavior is unchanged from before the
/// jsonb_build_object optimization was introduced.
/// </summary>
internal sealed class SelectProjectionNotSimpleException: MartenException
{
}

[UnconditionalSuppressMessage("Trimming", "IL2075",
    Justification = "Class-level: PublicMethods/PublicProperties access via a Type obtained from object.GetType() / GetGenericArguments. Source instance is preserved at the StoreOptions / projection-registration boundary.")]
internal class SelectParser: ExpressionVisitor
{
    private readonly ISerializer _serializer;
    private readonly IQueryableMemberCollection _members;
    private string _currentField;

    public SelectParser(ISerializer serializer, IQueryableMemberCollection members, Expression expression)
    {
        NewObject = new NewObject(serializer);
        _serializer = serializer;
        _members = members;
        Visit(expression);
    }

    public NewObject NewObject { get; private set; }

    public override Expression Visit(Expression node)
    {
        return base.Visit(node);
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.TryToParseConstant(out var constant))
        {
            VisitConstant(constant);
            return null;
        }

        switch (node.NodeType)
        {
            case ExpressionType.ArrayIndex:
                var index = (int)node.Right.ReduceToConstant().Value;

                var inner = _members.MemberFor(node.Left);
                if (inner is IHasChildrenMembers parent)
                {
                    var member = parent.FindMember(new ArrayIndexMember(index));
                    NewObject.Members[_currentField] = member;

                    _currentField = null;
                }
                else
                {
                    throw new BadLinqExpressionException("Marten is not (yet) able to process this Select() transform");
                }

                return null;
        }

        // Any other binary node reaching here is arithmetic/computation (Add, Subtract,
        // string concatenation via '+', comparisons used as a projected bool, etc.) that
        // can't be proven equivalent to a jsonb_build_object() expression. Signal the
        // caller to fall back to a client-side compiled transform (GH-5011) instead of
        // silently dropping the operation and returning the raw member value.
        throw new SelectProjectionNotSimpleException();
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        // GH-5031: A no-op / reference / value-preserving-widening Convert leaves the
        // underlying member's raw JSON value equal to the converted value, so we can keep
        // translating the operand into the jsonb_build_object() SELECT list (and preserve
        // streamability). Only genuinely lossy or computed conversions -- narrowing,
        // enum<->string, Convert.ToXxx, negation, logical-not, etc. -- fall back to the
        // client-side compiled transform (GH-5011).
        if (isSafeSelectConversion(node))
        {
            Visit(node.Operand);
            return null;
        }

        throw new SelectProjectionNotSimpleException();
    }

    // Rank of the primitive numeric types for widening checks. Higher == wider.
    private static readonly Dictionary<Type, int> _numericRank = new()
    {
        [typeof(byte)] = 1, [typeof(sbyte)] = 1,
        [typeof(short)] = 2, [typeof(ushort)] = 2,
        [typeof(int)] = 3, [typeof(uint)] = 3,
        [typeof(long)] = 4, [typeof(ulong)] = 4,
        [typeof(float)] = 5,
        [typeof(double)] = 6,
        [typeof(decimal)] = 7
    };

    private bool isSafeSelectConversion(UnaryExpression node)
    {
        // Only Convert/ConvertChecked can be safe; negation, logical-not, TypeAs, etc. are computed.
        if (node.NodeType != ExpressionType.Convert && node.NodeType != ExpressionType.ConvertChecked)
        {
            return false;
        }

        var source = node.Operand.Type;
        var target = node.Type;

        // Identity conversion.
        if (source == target)
        {
            return true;
        }

        // Boxing / reference upcast (T -> object, T -> base/interface): the stored JSON is unchanged.
        if (!target.IsValueType && target.IsAssignableFrom(source))
        {
            return true;
        }

        var sourceUnderlying = Nullable.GetUnderlyingType(source) ?? source;
        var targetUnderlying = Nullable.GetUnderlyingType(target) ?? target;

        // Nullable wrapping (int -> int?) or unwrap to the same underlying type.
        if (sourceUnderlying == targetUnderlying)
        {
            return true;
        }

        // enum -> its underlying integral, only when enums are persisted as integers (so the
        // raw JSON already holds the integral value). Under EnumStorage.AsString the JSON is
        // the name and the conversion is NOT value-preserving, so fall back.
        if (sourceUnderlying.IsEnum
            && _serializer.EnumStorage == Weasel.Core.EnumStorage.AsInteger
            && targetUnderlying == Enum.GetUnderlyingType(sourceUnderlying))
        {
            return true;
        }

        // Value-preserving numeric widening (int -> long, int -> decimal, float -> double, ...).
        return isWideningNumeric(sourceUnderlying, targetUnderlying);
    }

    private static bool isWideningNumeric(Type source, Type target)
    {
        if (!_numericRank.TryGetValue(source, out var sourceRank)
            || !_numericRank.TryGetValue(target, out var targetRank))
        {
            return false;
        }

        // Integer -> wider integer (int -> long): strictly wider rank within the integer range.
        // Integer -> floating/decimal (int -> double/decimal) and float -> double are widening too.
        // Everything else (narrowing, floating/double -> decimal, same-rank sign flips) falls back.
        const int integerCeiling = 4; // long/ulong

        if (sourceRank <= integerCeiling)
        {
            return targetRank > sourceRank;
        }

        // float (5) -> double (6) only; double/decimal never widen to something value-preserving here.
        return source == typeof(float) && target == typeof(double);
    }

    protected override Expression VisitConditional(ConditionalExpression node)
    {
        // Ternary/conditional expressions are computed expressions; fall back to a
        // client-side compiled transform (GH-5011).
        throw new SelectProjectionNotSimpleException();
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        var value = node.ReduceToConstant();
        var raw = value.Value;
        if (raw is string r)
        {
            // The projected constant is a runtime value (ReduceToConstant evaluates captured
            // locals), so it can be attacker-influenced. It is emitted verbatim into the
            // jsonb_build_object SELECT list, so escape embedded single quotes to keep it as
            // data and prevent SQL injection — mirroring Ordering.BuildNgramRankExpression.
            var literal = r.TrimStart('"').TrimEnd('"').Replace("'", "''");
            NewObject.Members[_currentField] = new LiteralSql($"'{literal}'");
        }
        else if (raw is null)
        {
            NewObject.Members[_currentField] = new LiteralSql("''");
        }
        else
        {
            // The projected constant is a runtime value (ReduceToConstant evaluates captured
            // locals), so it can be attacker-influenced. A non-string CLR type has no
            // SQL-safe ToString() guarantee (e.g. a JsonElement bound from a request body),
            // so bind it as a command parameter instead of inlining ToString() into the SQL.
            NewObject.Members[_currentField] = new ConstantParameterSql(raw);
        }

        _currentField = null;

        return base.VisitConstant(node);
    }

    protected override Expression VisitMemberInit(MemberInitExpression node)
    {
        var child = new SelectParser(_serializer, _members, node.NewExpression);
        foreach (var binding in node.Bindings.OfType<MemberAssignment>())
        {
            child.ReadBinding(binding);
        }

        if (_currentField == null)
        {
            // It's from an x => new Person{Age = x.Number, Name = x.Name} kind
            // of transform, so use the child's new object
            NewObject = child.NewObject;
        }
        else
        {
            NewObject.Members[_currentField] = child.NewObject;
        }

        return null;
    }

    public void ReadBinding(MemberAssignment binding)
    {
        _currentField = binding.Member.Name;
        Visit(binding.Expression);
    }

    protected override MemberBinding VisitMemberBinding(MemberBinding node)
    {
        _currentField = node.Member.Name;

        return base.VisitMemberBinding(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name == nameof(QueryableExtensions.ExplicitSql))
        {
            var sql = (string)node.Arguments.Last().ReduceToConstant().Value;
            if (_currentField != null)
            {
                NewObject.Members[_currentField] = new LiteralSql(sql);
                return null;
            }
        }

        // Any other method call (string.ToUpper(), custom methods, LINQ helpers, etc.)
        // is a computed expression that jsonb_build_object() cannot reproduce. Signal the
        // caller to fall back to a client-side compiled transform (GH-5011) instead of
        // silently dropping the call and returning the raw member value underneath it.
        throw new SelectProjectionNotSimpleException();
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (_currentField == null) return base.VisitMember(node);

        if (node.TryToParseConstant(out var constant))
        {
            VisitConstant(constant);
            return null;
        }

        var member = _members.MemberFor(node);
        NewObject.Members[_currentField] = member;
        _currentField = null;

        return base.VisitMember(node);
    }

    private bool _hasStarted;

    protected override Expression VisitNew(NewExpression node)
    {
        if (_hasStarted)
        {
            var child = new SelectParser(_serializer, _members, node);
            NewObject.Members[_currentField] = child.NewObject;

            return null;
        }

        _hasStarted = true;

        var parameters = node.Constructor.GetParameters();

        for (var i = 0; i < parameters.Length; i++)
        {
            _currentField = ResolveFieldName(node, parameters, i);
            Visit(node.Arguments[i]);
        }

        return node;
    }

    /// <summary>
    /// Resolves the field name for a constructor parameter in a NewExpression.
    /// Prefers NewExpression.Members (populated for C# anonymous types), then falls back
    /// to matching properties by position for F# records where constructor parameters
    /// are camelCase but properties are PascalCase.
    /// </summary>
    internal static string ResolveFieldName(NewExpression node, System.Reflection.ParameterInfo[] parameters, int index)
    {
        // Prefer NewExpression.Members when available (C# anonymous types, F# anonymous records)
        if (node.Members != null && index < node.Members.Count)
        {
            return node.Members[index].Name;
        }

        // For F# records, constructor params are camelCase but properties are PascalCase.
        // F# records are marked with CompilationMappingAttribute(SourceConstructFlags.RecordType).
        // Match by position since F# guarantees property order matches constructor parameter order.
        if (IsFSharpRecord(node.Type))
        {
            var properties = node.Type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (index < properties.Length &&
                string.Equals(properties[index].Name, parameters[index].Name, System.StringComparison.OrdinalIgnoreCase))
            {
                return properties[index].Name;
            }
        }

        return parameters[index].Name;
    }

    private static bool IsFSharpRecord(System.Type type)
    {
        return type.GetCustomAttributes(false)
            .Any(a => a.GetType().FullName == "Microsoft.FSharp.Core.CompilationMappingAttribute"
                      && a.GetType().GetProperty("SourceConstructFlags")?.GetValue(a)?.ToString() == "RecordType");
    }
}


public interface ISelectableMember
{
    void Apply(ICommandBuilder builder, ISerializer serializer);
}

internal class NewObject : ISqlFragment
{
    private readonly ISerializer _serializer;

    public NewObject(ISerializer serializer)
    {
        _serializer = serializer;
    }

    public Dictionary<string, ISqlFragment> Members { get; } = new();

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(" jsonb_build_object(");

        var pairs = Members.ToArray();
        for (int i = 0; i < pairs.Length - 1; i++)
        {
            writeMember(builder, pairs[i]);
            builder.Append(", ");
        }

        writeMember(builder, pairs.Last());

        builder.Append(") ");
    }

    private void writeMember(ICommandBuilder builder, KeyValuePair<string, ISqlFragment> pair)
    {
        builder.Append($"'{pair.Key.FormatCase(_serializer.Casing)}', ");
        if (pair.Value is ISelectableMember selectable)
        {
            selectable.Apply(builder, _serializer);
        }
        else
        {
            pair.Value.Apply(builder);
        }
    }

}
