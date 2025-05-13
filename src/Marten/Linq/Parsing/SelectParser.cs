#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.SqlGeneration;
using Marten.Util;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing;

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

        return base.VisitBinary(node);
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        var value = node.ReduceToConstant();
        var raw = value.Value;
        if (raw is string r)
        {
            NewObject.Members[_currentField] = new LiteralSql($"'{r.TrimStart('"').TrimEnd('"')}'");
        }
        else if (raw is null)
        {
            NewObject.Members[_currentField] = new LiteralSql("''");
        }
        else
        {
            NewObject.Members[_currentField] = new LiteralSql(raw.ToString());
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
        if (!MethodBeCanTranslated(node.Method))
        {
            ThrowForUnsupportedMethod(node);
        }

        if (node.Method.Name == nameof(QueryableExtensions.ExplicitSql))
        {
            var sql = (string)node.Arguments.Last().ReduceToConstant().Value;
            if (_currentField != null)
            {
                NewObject.Members[_currentField] = new LiteralSql(sql);
                return null;
            }
        }

        return base.VisitMethodCall(node);
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
        if (!_hasStarted)
        {
            if (node.Arguments.Count > 0 && !IsAllowedConstructor(node))
            {
                throw new BadLinqExpressionException(
                    $"Marten cannot translate projecting into '{node.Type.Name}'. " +
                    "Only anonymous types, parameterless constructors, or constructors marked with [JsonConstructor] are supported.");
            }

            _hasStarted = true;

            var parameters = node.Constructor.GetParameters();

            for (var i = 0; i < parameters.Length; i++)
            {
                _currentField = parameters[i].Name;
                Visit(node.Arguments[i]);
            }

            return node;
        }

        if (node.Arguments.Count > 0 && !IsAllowedConstructor(node))
        {
            throw new BadLinqExpressionException(
                $"Marten cannot translate constructing '{node.Type.Name}' inside a Select().");
        }

        var child = new SelectParser(_serializer, _members, node);
        NewObject.Members[_currentField] = child.NewObject;
        _currentField = null;
        return null;
    }

    private static bool IsAllowedConstructor(NewExpression node)
    {
        if (node.Arguments.Count == 0)
            return true; // parameterless
        if (IsAnonymousType(node.Type))
            return true;

        var ctor = node.Constructor;
        return ctor.GetCustomAttributes(typeof(Newtonsoft.Json.JsonConstructorAttribute), true).Any()
            || ctor.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonConstructorAttribute), true).Any();
    }

    private static bool IsAnonymousType(Type type)
    {
        return Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute), false)
               && type.Name.Contains("AnonymousType")
               && type.IsGenericType;
    }

    private static bool MethodBeCanTranslated(MethodInfo method)
    {
        if (method.Name == nameof(QueryableExtensions.ExplicitSql))
        {
            return true;
        }

        return false;
    }

    public static void ThrowForUnsupportedMethod(MethodCallExpression node, Exception inner = null)
    {
        throw new BadLinqExpressionException(
            $"Marten cannot translate the method call '{node.Method.DeclaringType?.Name}.{node.Method.Name}' " +
            "in Select() projections to SQL. Consider using only Marten-supported operations or " +
            "use CustomProjection() for raw SQL fragments.", inner);
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
