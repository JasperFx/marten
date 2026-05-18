using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Core.Reflection;
using Marten.Events.CodeGeneration;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Linq.Includes;
using Marten.Linq.QueryHandlers;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Internal.CompiledQueries;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2067",
    Justification = "Class-level: parameter receives a DAM-annotated Type from a reflective lookup whose source type is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("Trimming", "IL2070",
    Justification = "Class-level: reflects PublicMethods/PublicProperties on a Type whose runtime instance is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("Trimming", "IL2072",
    Justification = "Class-level: assigns the result of a reflective Type/MethodInfo lookup into a DAM-annotated target. Source types are preserved at the registration boundary.")]
[UnconditionalSuppressMessage("Trimming", "IL2075",
    Justification = "Class-level: PublicMethods/PublicProperties access via a Type obtained from object.GetType() / GetGenericArguments. Source instance is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public class CompiledQueryPlan : ICommandBuilder
{
    public Type QueryType { get; }
    public Type OutputType { get; }
    public const string ParameterPlaceholder = "^";

    public List<MemberInfo> InvalidMembers { get; } = new();
    public List<IQueryMember> QueryMembers { get; } = new();
    public List<MemberInfo> IncludeMembers { get; } = new();
    internal List<IIncludePlan> IncludePlans { get; } = new();

    private readonly List<CommandPlan> _commands = new();

    /// <summary>
    /// The command plans derived by <c>LinqQueryParser</c> on first call. Each plan
    /// holds the SQL skeleton + a list of <see cref="ParameterUsage"/> entries that
    /// say which query-type member supplies each parameter's value. Iteration 3
    /// of #4405 reads this list from <c>SourceGeneratedCompiledQueryHandler</c> to
    /// replay the commands against the source-gen-emitted binder at runtime — the
    /// AOT-safe replacement for codegen'ing <c>ConfigureCommand</c> bodies per
    /// query type.
    /// </summary>
    internal IReadOnlyList<CommandPlan> Commands => _commands;

    public IQueryHandler HandlerPrototype { get; set; }
    public MemberInfo? StatisticsMember { get; set; }

    public CompiledQueryPlan(Type queryType, Type outputType)
    {
        QueryType = queryType;
        OutputType = outputType;

        sortMembers();
    }

    #region finding members on query type

    private void sortMembers()
    {
        var members = findMembers().ToArray();
        if (members.Length == 0)
        {
            Debug.WriteLine(
                "No public properties or fields found. Sorry, but Marten cannot use primary constructor values as compiled query parameters at this time, use a class with settable properties instead.");
        }

        foreach (var member in members)
        {
            var memberType = member.GetRawMemberType();
            if (memberType == typeof(QueryStatistics))
            {
                StatisticsMember = member;
            }

            else if (memberType.Closes(typeof(IDictionary<,>)))
            {
                IncludeMembers.Add(member);
            }
            else if (memberType.Closes(typeof(Action<>)))
            {
                IncludeMembers.Add(member);
            }
            else if (memberType.IsArray && QueryCompiler.Finders.Any(x => x.Matches(memberType.GetElementType()!)))
            {
                // Arrays like string[], int[], Guid[] etc. whose element type has a registered
                // parameter finder should be treated as query parameters, NOT as include members.
                // This check must come before the IList<> check since arrays implement IList<T>.
                if (member is PropertyInfo)
                {
                    var queryMember = typeof(PropertyQueryMember<>).CloseAndBuildAs<IQueryMember>(member, memberType);
                    QueryMembers.Add(queryMember);
                }
                else if (member is FieldInfo)
                {
                    var queryMember = typeof(FieldQueryMember<>).CloseAndBuildAs<IQueryMember>(member, memberType);
                    QueryMembers.Add(queryMember);
                }
            }
            else if (memberType.Closes(typeof(IList<>)))
            {
                IncludeMembers.Add(member);
            }
            else if (memberType.IsNullable())
            {
                InvalidMembers.Add(member);
            }
            else if (QueryCompiler.Finders.All(x => !x.Matches(memberType)))
            {
                InvalidMembers.Add(member);
            }
            else if (member is PropertyInfo)
            {
                var queryMember = typeof(PropertyQueryMember<>).CloseAndBuildAs<IQueryMember>(member, memberType);
                QueryMembers.Add(queryMember);
            }
            else if (member is FieldInfo)
            {
                var queryMember = typeof(FieldQueryMember<>).CloseAndBuildAs<IQueryMember>(member, memberType);
                QueryMembers.Add(queryMember);
            }
        }
    }

    private IEnumerable<MemberInfo> findMembers()
    {
        foreach (var field in QueryType.GetFields(BindingFlags.Instance | BindingFlags.Public)
                     .Where(x => !x.HasAttribute<MartenIgnoreAttribute>())) yield return field;

        foreach (var property in QueryType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(x => !x.HasAttribute<MartenIgnoreAttribute>())) yield return property;
    }

    #endregion

    public void AddParameters<T>(IDictionary<string, T> parameters)
    {
        throw new NotImplementedException();
    }

    public string TenantId { get; set; }

    #region ICommandBuilder implementation

    private CommandPlan _current;

    string ICommandBuilder.LastParameterName => _current.Parameters.LastOrDefault()?.Parameter.ParameterName;

    private CommandPlan appendCommand()
    {
        var plan = new CommandPlan();
        _commands.Add(plan);

        return plan;
    }

    void ICommandBuilder.Append(string sql)
    {
        _current ??= appendCommand();

        _current.CommandText += sql;
    }

    void ICommandBuilder.Append(char character)
    {
        _current ??= appendCommand();
        _current.CommandText += character;
    }

    NpgsqlParameter ICommandBuilder.AppendParameter<T>(T value)
    {
        _current ??= appendCommand();
        var name = "p" + _parameterIndex;
        _parameterIndex++;
        var usage = new ParameterUsage(_current.Parameters.Count, name, value);
        _current.Parameters.Add(usage);

        _current.CommandText += ParameterPlaceholder;

        return usage.Parameter;
    }

    NpgsqlParameter ICommandBuilder.AppendParameter<T>(T value, NpgsqlDbType dbType)
    {
        _current ??= appendCommand();
        var name = "p" + _parameterIndex;
        _parameterIndex++;
        var usage = new ParameterUsage(_current.Parameters.Count, name, value, dbType);

        _current.Parameters.Add(usage);

        _current.CommandText += ParameterPlaceholder;

        return usage.Parameter;
    }

    private int _parameterIndex = 0;

    NpgsqlParameter ICommandBuilder.AppendParameter(object value)
    {
        _current ??= appendCommand();
        var name = "p" + _parameterIndex;
        _parameterIndex++;
        var usage = new ParameterUsage(_current.Parameters.Count, name, value);
        _current.Parameters.Add(usage);

        _current.CommandText += ParameterPlaceholder;

        return usage.Parameter;
    }

    NpgsqlParameter ICommandBuilder.AppendParameter(object? value, NpgsqlDbType? dbType)
    {
        return appendParameter(value, dbType);
    }

    private NpgsqlParameter appendParameter(object value, NpgsqlDbType? dbType)
    {
        _current ??= appendCommand();
        var name = "p" + _parameterIndex;
        _parameterIndex++;
        var usage = new ParameterUsage(_current.Parameters.Count, name, value, dbType);
        _current.Parameters.Add(usage);

        _current.CommandText += ParameterPlaceholder;

        return usage.Parameter;
    }

    void ICommandBuilder.AppendParameters(params object[] parameters)
    {
        _current ??= appendCommand();
        throw new NotSupportedException();
    }

    public IGroupedParameterBuilder CreateGroupedParameterBuilder(char? seperator = null)
    {
        throw new NotSupportedException();
    }

    NpgsqlParameter[] ICommandBuilder.AppendWithParameters(string text)
    {
        _current ??= appendCommand();
        var split = text.Split('?');
        var parameters = new NpgsqlParameter[split.Length - 1];

        _current.CommandText += split[0];
        for (var i = 0; i < parameters.Length; i++)
        {
            // Just need a placeholder parameter type and value
            var parameter = appendParameter(DBNull.Value, NpgsqlDbType.Text);
            parameters[i] = parameter;
            _current.CommandText += split[i + 1];
        }

        return parameters;
    }

    NpgsqlParameter[] ICommandBuilder.AppendWithParameters(string text, char placeholder)
    {
        _current ??= appendCommand();
        var split = text.Split(placeholder);
        var parameters = new NpgsqlParameter[split.Length - 1];

        _current.CommandText += split[0];
        for (var i = 0; i < parameters.Length; i++)
        {
            // Just need a placeholder parameter type and value
            var parameter = appendParameter(DBNull.Value, NpgsqlDbType.Text);
            parameters[i] = parameter;
            _current.CommandText += split[i + 1];
        }

        return parameters;
    }

    void ICommandBuilder.StartNewCommand()
    {
        _current = appendCommand();
    }

    void ICommandBuilder.AddParameters(object parameters)
    {
        throw new NotSupportedException(
            "No, just no. Marten does not support parameters via anonymous objects in compiled queries");
    }

    public void AddParameters(IDictionary<string, object?> parameters)
    {
        throw new NotImplementedException();
    }

    #endregion

    public QueryStatistics? GetStatisticsIfAny(object query)
    {
        if (StatisticsMember is PropertyInfo p)
        {
            return (QueryStatistics?)p.GetValue(query) ?? new QueryStatistics();
        }

        if (StatisticsMember is FieldInfo f)
        {
            return (QueryStatistics?)f.GetValue(query) ?? new QueryStatistics();
        }

        return null;
    }

    public ICompiledQuery<TDoc, TOut> CreateQueryTemplate<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query) where TDoc : notnull
    {
        foreach (var parameter in QueryMembers) parameter.StoreValue(query);

        if (query is not IQueryPlanning && areAllMemberValuesUnique(query))
        {
            return query;
        }

        try
        {
            return (ICompiledQuery<TDoc, TOut>)TryCreateUniqueTemplate(query.GetType());
        }
        catch (Exception e)
        {
            throw new InvalidCompiledQueryException("Unable to create a Compiled Query template", e);
        }
    }

    public object TryCreateUniqueTemplate(Type type)
    {
        var constructor = type.GetConstructors().MaxBy(x => x.GetParameters().Length);


        if (constructor == null)
        {
            throw new InvalidOperationException("Cannot find a suitable constructor for query planning for type " +
                                                type.FullNameInCode());
        }

        var valueSource = new UniqueValueSource();

        var ctorArgs = valueSource.ArgsFor(constructor);
        var query = Activator.CreateInstance(type, ctorArgs);
        if (query is IQueryPlanning planning)
        {
            planning.SetUniqueValuesForQueryPlanning();
            foreach (var member in QueryMembers) member.StoreValue(query);
        }

        if (areAllMemberValuesUnique(query))
        {
            return query;
        }

        foreach (var queryMember in QueryMembers) queryMember.TryWriteValue(valueSource, query);

        if (areAllMemberValuesUnique(query))
        {
            return query;
        }

        throw new InvalidCompiledQueryException("Marten is unable to create a compiled query plan for type " +
                                                type.FullNameInCode());
    }

    private bool areAllMemberValuesUnique(object query)
    {
        return QueryCompiler.Finders.All(x => x.AreValuesUnique(query, this));
    }

    public void MatchParameters(StoreOptions options, ICompiledQueryAwareFilter[] filters)
    {
        foreach (var commandPlan in _commands)
        {
            foreach (var usage in commandPlan.Parameters)
            {
                if (usage.Parameter.Value.Equals(TenantId))
                {
                    usage.IsTenant = true;
                }
                else
                {
                    foreach (var queryMember in QueryMembers)
                    {
                        if (queryMember.TryMatch(usage.Parameter, options, filters, out var filter))
                        {
                            usage.Member = queryMember;
                            usage.Filter = filter;
                            // Build the runtime setter once per (filter, query-member)
                            // pair so the AOT/source-gen path can write the parameter
                            // without going back through the codegen-emit hook. The
                            // codegen path is unaffected — it still drives off
                            // ParameterUsage.GenerateCode until Phase 1E.
                            if (filter is not null)
                            {
                                usage.Setter = filter.BuildSetter();
                            }
                            break;
                        }
                    }
                }
            }
        }
    }

    public void GenerateCode(GeneratedMethod method, StoreOptions storeOptions)
    {
        int number = 1;
        foreach (var command in _commands)
        {
            if (number != 1)
            {
                method.Frames.Code($"{{0}}.{nameof(ICommandBuilder.StartNewCommand)}();", Use.Type<ICommandBuilder>());
            }

            var parameters = $"parameters{number}";

            if (command.Parameters.Any())
            {
                method.Frames.Code($"var {parameters} = {{0}}.{nameof(CommandBuilder.AppendWithParameters)}(@{{1}}, '{ParameterPlaceholder}');",
                    Use.Type<ICommandBuilder>(), command.CommandText);

                foreach (var usage in command.Parameters)
                {
                    usage.GenerateCode(method, parameters, storeOptions);
                }
            }
            else
            {
                method.Frames.Code($"{{0}}.Append({{1}});", Use.Type<ICommandBuilder>(), command.CommandText);
            }

            number++;
        }
    }
}
