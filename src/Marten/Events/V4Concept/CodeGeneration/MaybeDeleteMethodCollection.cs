using System;
using System.Reflection;

namespace Marten.Events.V4Concept.CodeGeneration
{
    internal class MaybeDeleteMethodCollection : MethodCollection
    {
        private readonly Type _identityType;
        public Type AggregateType { get; }
        public static readonly string MethodName = "MaybeDelete";

        public MaybeDeleteMethodCollection(Type projectionType, Type aggregateType, Type identityType) : base(MethodName, projectionType)
        {
            _identityType = identityType;
            AggregateType = aggregateType;
        }

        public override IEventHandlingFrame CreateAggregationHandler(Type aggregateType, MethodInfo method)
        {
            return new MaybeDeleteFrame(ProjectionType, aggregateType, _identityType, method);
        }
    }
}
