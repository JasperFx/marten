using System;
using System.Reflection;
using Marten.Schema;

namespace Marten.Events.V4Concept.CodeGeneration
{
    internal class ShouldDeleteMethodCollection : MethodCollection
    {
        public Type AggregateType { get; }
        public static readonly string MethodName = "ShouldDelete";

        public ShouldDeleteMethodCollection(Type projectionType, Type aggregateType) : base(MethodName, projectionType)
        {
            AggregateType = aggregateType;
        }

        public override IEventHandlingFrame CreateEventTypeHandler(Type aggregateType,
            DocumentMapping aggregateMapping, MethodInfo method)
        {
            return new MaybeDeleteFrame(ProjectionType, aggregateType, aggregateMapping.IdType, method);
        }
    }
}
