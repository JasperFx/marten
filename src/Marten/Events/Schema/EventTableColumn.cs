using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using Marten.Internal.CodeGeneration;
using Marten.Linq.Parsing;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema
{
    internal class EventTableColumn: TableColumn, IEventTableColumn
    {
        private readonly Expression<Func<IEvent, object>> _eventMemberExpression;
        private readonly MemberInfo _member;

        public EventTableColumn(string name, Expression<Func<IEvent, object>> eventMemberExpression) : base(name, "varchar")
        {
            _eventMemberExpression = eventMemberExpression;
            _member = FindMembers.Determine(eventMemberExpression).Single();
            var memberType = _member.GetMemberType();
            Type = PostgresqlProvider.Instance.GetDatabaseType(memberType, EnumStorage.AsInteger);
            NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(memberType);
        }

        public NpgsqlDbType NpgsqlDbType { get; set; }

        public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
        {
            method.IfDbReaderValueIsNotNull(index, () =>
            {
                method.AssignMemberFromReader(null, index, _eventMemberExpression);
            });
        }

        public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
        {
            method.IfDbReaderValueIsNotNull(index, () =>
            {
                method.AssignMemberFromReader(null, index, _eventMemberExpression);
            });
        }

        public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index)
        {
            method.Frames.Code($"parameters[{index}].{nameof(NpgsqlParameter.NpgsqlDbType)} = {{0}};",
                NpgsqlDbType);
            method.Frames.Code(
                $"parameters[{index}].{nameof(NpgsqlParameter.Value)} = {{0}}.{_member.Name};", Use.Type<IEvent>());
        }
    }
}
