using System;
using System.Linq;
using System.Reflection;
using Baseline;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Events.CodeGeneration;
using Marten.Util;
using Weasel.Postgresql.Tables;

#nullable enable

namespace Marten.Events.Projections.Flattened
{
    internal class DeleteRowFrame : MethodCall, IEventHandlingFrame
    {
        private readonly Table _table;
        private readonly MemberInfo[] _members;
        private Variable? _event;

        public DeleteRowFrame(Table table, Type eventType, MemberInfo[] members) : base(typeof(IDocumentOperations), nameof(IDocumentOperations.QueueSqlCommand))
        {
            if (!members.Any()) throw new ArgumentOutOfRangeException(nameof(members), "Empty member list");

            EventType = eventType;
            _table = table;
            _members = members;
        }

        public void Configure(EventProcessingFrame parent)
        {
            _event = parent.SpecificEvent;

            var sql = $"delete from {_table.Identifier} where {_table.PrimaryKeyColumns.Single()} = ?";
            Arguments[0] = Constant.ForString(sql);

            // TODO -- need a formal derived variable with this usage
            Arguments[1] = new Variable(_members.Last().GetMemberType(), $"{_event.Usage}.{_members.Select(x => x.Name).Join(".")}");
        }

        public Type EventType { get; }
    }
}
