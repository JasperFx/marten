#nullable enable
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.MatchesSql;

public class MatchesSqlParser: IMethodCallParser
{
    private static readonly MethodInfo _sqlMethod =
        typeof(MatchesSqlExtensions).GetMethod(nameof(MatchesSqlExtensions.MatchesSql),
            new[] { typeof(object), typeof(string), typeof(object[]) })!;

    private static readonly MethodInfo _sqlMethodWithPlaceholder =
        typeof(MatchesSqlExtensions).GetMethod(nameof(MatchesSqlExtensions.MatchesSql),
            new[] { typeof(object), typeof(char), typeof(string), typeof(object[]) })!;

    private static readonly MethodInfo _fragmentMethod =
        typeof(MatchesSqlExtensions).GetMethod(nameof(MatchesSqlExtensions.MatchesSql),
            new[] { typeof(object), typeof(ISqlFragment) })!;

    public bool Matches(MethodCallExpression expression)
    {
        return Equals(expression.Method, _sqlMethod) || Equals(expression.Method, _sqlMethodWithPlaceholder) || Equals(expression.Method, _fragmentMethod);
    }

    public ISqlFragment? Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        if (expression.Method.Equals(_sqlMethod))
        {
            return new WhereFragment(expression.Arguments[1].Value().As<string>(),
                expression.Arguments[2].Value().As<object[]>());
        }

        if (expression.Method.Equals(_sqlMethodWithPlaceholder))
        {
            return new CustomizableWhereFragment(expression.Arguments[2].Value().As<string>(),
                expression.Arguments[1].Value().As<char>().ToString(),
                expression.Arguments[3].Value().As<object[]>());
        }

        if (expression.Method.Equals(_fragmentMethod))
        {
            return expression.Arguments[1].Value() as ISqlFragment;
        }

        return null;
    }
}

public class MatchesJsonPathParser: IMethodCallParser
{
    private static readonly MethodInfo _sqlMethod =
        typeof(MatchesSqlExtensions).GetMethod(nameof(MatchesSqlExtensions.MatchesJsonPath),
            new[] { typeof(object), typeof(string), typeof(object[]) })!;

    public bool Matches(MethodCallExpression expression)
    {
        return Equals(expression.Method, _sqlMethod);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        // Raw values, matching MatchesSqlParser. LiteralSqlWithJsonPath accepts either shape.
        var arguments = expression.Arguments[2].Value().As<object[]>();

        return new LiteralSqlWithJsonPath(expression.Arguments[1].Value().As<string>(), arguments);
    }
}

internal class LiteralSqlWithJsonPath : ISqlFragment
{
    private readonly string _sql;
    private readonly object[] _parameters;

    public LiteralSqlWithJsonPath(string sql, object[] parameters)
    {
        _sql = sql;
        _parameters = parameters;
    }

    public void Apply(ICommandBuilder builder)
    {
        var parameters = builder.AppendWithParameters(_sql, '^');

        if (parameters.Length != _parameters.Length)
        {
            // #5289 follow-up. Otherwise this is an IndexOutOfRangeException from inside the LINQ
            // provider, or -- worse, when there are more values than placeholders -- silence, with
            // the surplus values simply never reaching the query. The '^' is easy to get wrong
            // because it is also a regex anchor, so it appears inside JSONPath literals by accident.
            throw new BadLinqExpressionException(
                $"MatchesJsonPath was given {_parameters.Length} parameter(s) but the SQL has {parameters.Length} '^' placeholder(s). "
                + "Note that '^' is the placeholder character for this overload, so a '^' inside a JSONPath literal "
                + "(a like_regex anchor, for instance) counts as one. SQL: " + _sql);
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            // #5289 follow-up: mirrors CustomizableWhereFragment.Apply, which is what MatchesSql uses.
            // AppendWithParameters seeds every placeholder with DBNull.Value and the provider's STRING
            // parameter type, because at that point it has no idea what is going into it. Assigning
            // only .Value leaves NpgsqlDbType at Text, so any non-string argument threw
            // "Writing values of 'System.Int32' is not supported for parameters having NpgsqlDbType
            // 'Text'" -- the same failure #5289 set out to fix -- and a null argument overwrote the
            // seeded DBNull with a CLR null, which Npgsql rejects outright.
            //
            // The unwrap keeps a caller-built CommandParameter working, so both shapes are accepted
            // and this does not depend on what MatchesJsonPathParser.Parse happens to hand over.
            var commandParameter = _parameters[i] as CommandParameter ?? new CommandParameter(_parameters[i]);
            parameters[i].Value = commandParameter.Value;
            if (commandParameter.DbType.HasValue)
            {
                parameters[i].NpgsqlDbType = commandParameter.DbType.Value;
            }
        }
    }
}
