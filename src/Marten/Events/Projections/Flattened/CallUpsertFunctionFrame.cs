using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Events.CodeGeneration;
using Weasel.Core;

namespace Marten.Events.Projections.Flattened
{
    internal class CallUpsertFunctionFrame: MethodCall, IEventHandlingFrame
    {
        private readonly DbObjectName _functionIdentifier;
        private readonly List<IColumnMap> _columnMaps;
        private readonly MemberInfo[] _members;

        public CallUpsertFunctionFrame(Type eventType, DbObjectName functionIdentifier, List<IColumnMap> columnMaps,
            MemberInfo[] members) : base(typeof(IDocumentOperations), nameof(IDocumentOperations.QueueSqlCommand))
        {
            _functionIdentifier = functionIdentifier;
            _columnMaps = columnMaps;
            _members = members;
            EventType = eventType;
        }

        public void Configure(EventProcessingFrame parent)
        {
            var pk = $"{parent.SpecificEvent.Usage}.{_members.Select(x => x.Name).Join(".")}";
            var sql = $"select {_functionIdentifier.QualifiedName}(?, {_columnMaps.Where(x => x.RequiresInput).Select(x => "?").Join(", ")});";

            var values = _columnMaps.Where(x => x.RequiresInput).Select(x => x.ToValueAccessorCode(parent.SpecificEvent)).Join(", ");

            Arguments[0] = Constant.ForString(sql);
            Arguments[1] = new Variable(typeof(object[]), $"{pk}, {values}");
        }

        public Type EventType { get; }
    }
}
