#nullable enable
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Util;

namespace Marten.Linq.Parsing;

public sealed class JsonPathCreator(ISerializer serializer) : ExpressionVisitor
{
    private readonly StringBuilder _jsonPathBuilder = new();
    private readonly Stack<string> _fieldNames = new();
    private readonly HashSet<MemberExpression> _memberIfInUnary = [];
    private readonly HashSet<MemberExpression> _memberIfInBinary = [];
    private static readonly Dictionary<ExpressionType, string> LogicalOperators = new()
    {
        [ExpressionType.Equal] = "==",
        [ExpressionType.NotEqual] = "!=",
        [ExpressionType.GreaterThan] = ">",
        [ExpressionType.GreaterThanOrEqual] = ">=",
        [ExpressionType.LessThan] = "<",
        [ExpressionType.LessThanOrEqual] = "<=",
        [ExpressionType.And] = "&&",
        [ExpressionType.AndAlso] = "&&",
        [ExpressionType.Or] = "||",
        [ExpressionType.OrElse] = "||"
    };


    public string Build(Expression expression)
    {
        Visit(expression);

        var jsonPath = $"$ ? ({_jsonPathBuilder})";
        _jsonPathBuilder.Clear();

        return jsonPath;
    }

    
    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            if (node.Operand is MemberExpression operandMember)
                _memberIfInUnary.Add(operandMember);

            Visit(node.Operand);
        }

        return node;
    }

    protected override Expression VisitLambda<T>(Expression<T> node)
    {
        return Visit(node.Body);
    }
    protected override Expression VisitParameter(ParameterExpression node)
    {
        _jsonPathBuilder.Append("@");
        return node;
    }
    
    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.Left is MemberExpression leftMember)
            _memberIfInBinary.Add(leftMember);

        if (node.Right is MemberExpression rightMember)
            _memberIfInBinary.Add(rightMember);

        Visit(node.Left);
        _jsonPathBuilder.Append($" {LogicalOperators[node.NodeType]} ");
        Visit(node.Right);

        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression is { NodeType: ExpressionType.Constant or ExpressionType.MemberAccess})
        {
            _fieldNames.Push(node.Member.Name);
            Visit(node.Expression);
        }
        else
        {
            _jsonPathBuilder.Append($"@.{node.Member.Name.FormatCase(serializer.Casing)}");

            if (!_memberIfInBinary.Contains(node) && node.Type == typeof(bool))
                _jsonPathBuilder.Append($" {LogicalOperators[ExpressionType.Equal]} {(_memberIfInUnary.Contains(node) ? "false" : "true")}");
        }

        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        var value = GetValue(node.Value);
        _jsonPathBuilder.Append(GetFormatedValue(value));

        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
        => throw new MartenNotSupportedException("Calling a method is not supported");

    private string GetFormatedValue(object? value)
        => serializer.ToJson(value);

    private object? GetValue(object? input)
    {
        if (input is null)
            return null;

        var type = input.GetType();

        if (!type.IsClass || type == typeof(string))
            return input;

        var fieldName = _fieldNames.Pop();
        var fieldInfo = type.GetField(fieldName);

        var value = fieldInfo is not null
            ? fieldInfo.GetValue(input)
            : type.GetProperty(fieldName)?.GetValue(input);

        return GetValue(value);
    }
}
