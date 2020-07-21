using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using LamarCodeGeneration;
using Marten.Exceptions;
using Marten.Internal.Linq;
using Marten.Internal.Linq.Includes;
using Marten.Linq;
using Marten.Schema.Arguments;
using Marten.Util;
using Npgsql;

namespace Marten.Internal.CompiledQueries
{
    public class CompiledQueryPlan
    {
        public Type QueryType { get; }
        public Type OutputType { get; }

        public CompiledQueryPlan(Type queryType, Type outputType)
        {
            QueryType = queryType;
            OutputType = outputType;
        }

        public void FindMembers()
        {
            foreach (var member in findMembers())
            {
                if (IncludeMembers.Contains(member)) continue;

                var memberType = member.GetRawMemberType();
                if (memberType == typeof(QueryStatistics))
                {
                    StatisticsMember = member;
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
                    Parameters.Add(queryMember);
                }
                else if (member is FieldInfo)
                {
                    var queryMember = typeof(FieldQueryMember<>).CloseAndBuildAs<IQueryMember>(member, memberType);
                    Parameters.Add(queryMember);
                }
            }
        }

        private IEnumerable<MemberInfo> findMembers()
        {
            foreach (var field in QueryType.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                yield return field;
            }

            foreach (var property in QueryType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                yield return property;
            }
        }

        public IList<MemberInfo> InvalidMembers { get; } = new List<MemberInfo>();

        public IList<IQueryMember> Parameters { get; } = new List<IQueryMember>();



        public NpgsqlCommand Command { get; set; }

        public string CorrectedCommandText()
        {
            var text = Command.CommandText;

            for (var i = 0; i < Command.Parameters.Count; i++)
            {
                text = text.Replace(":p" + i, "?");
            }

            return text;
        }

        public IQueryHandler HandlerPrototype { get; set; }

        public MemberInfo StatisticsMember { get; set; }

        public IList<MemberInfo> IncludeMembers { get; } = new List<MemberInfo>();

        public IList<IIncludePlan> IncludePlans { get; } = new List<IIncludePlan>();


        public QueryStatistics GetStatisticsIfAny(object query)
        {
            if (StatisticsMember is PropertyInfo p) return (QueryStatistics)p.GetValue(query) ?? new QueryStatistics();

            if (StatisticsMember is FieldInfo f) return (QueryStatistics)f.GetValue(query) ?? new QueryStatistics();

            return null;
        }

        public ICompiledQuery<TDoc,TOut> CreateQueryTemplate<TDoc, TOut>(ICompiledQuery<TDoc,TOut> query)
        {
            foreach (var parameter in Parameters)
            {
                parameter.StoreValue(query);
            }

            if (AreAllMemberValuesUnique(query))
            {
                return query;
            }

            try
            {
                return (ICompiledQuery<TDoc,TOut>)TryCreateUniqueTemplate(query.GetType());
            }
            catch (Exception e)
            {
                throw new InvalidCompiledQueryException("Unable to create a Compiled Query template", e);
            }
        }

        private bool AreAllMemberValuesUnique(object query)
        {
            return QueryCompiler.Finders.All(x => x.AreValuesUnique(query, this));
        }

        public object TryCreateUniqueTemplate(Type type)
        {
            var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic)
                .OrderByDescending(x => x.GetParameters().Count())
                .FirstOrDefault();

            if (constructor == null)
            {
                throw new InvalidOperationException("Cannot find a suitable constructor for query planning for type " + type.FullNameInCode());
            }

            var valueSource = new UniqueValueSource();

            var ctorArgs = valueSource.ArgsFor(constructor);
            var query = Activator.CreateInstance(type, ctorArgs);

            if (AreAllMemberValuesUnique(query))
            {
                return query;
            }

            foreach (var queryMember in Parameters)
            {
                queryMember.TryWriteValue(valueSource, query);
            }

            if (AreAllMemberValuesUnique(query))
            {
                return query;
            }

            throw new InvalidOperationException("Marten is unable to create a compiled query plan for type " + type.FullNameInCode());
        }

        public void ReadCommand(NpgsqlCommand command, StoreOptions storeOptions)
        {
            Command = command;

            foreach (var parameter in Parameters)
            {
                parameter.TryMatch(command, storeOptions);
            }

            var missing = Parameters.Where(x => x.ParameterIndex < 0);
            if (missing.Any())
            {
                throw new  InvalidCompiledQueryException($"Unable to match compiled query member(s) {missing.Select(x => x.Member.Name).Join(", ")} with a command parameter");
            }
        }
    }
}
