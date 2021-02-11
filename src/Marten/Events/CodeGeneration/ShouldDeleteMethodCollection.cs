using System;
using System.Linq;
using Marten.Schema;

namespace Marten.Events.CodeGeneration
{
    internal class ShouldDeleteMethodCollection : MethodCollection
    {
        public static readonly string MethodName = "ShouldDelete";

        public ShouldDeleteMethodCollection(Type projectionType, Type aggregateType) : base(MethodName, projectionType, aggregateType)
        {
            _validArgumentTypes.Add(typeof(IQuerySession));
            _validArgumentTypes.Add(aggregateType);

            _validReturnTypes.Add(typeof(bool));
        }

        public override IEventHandlingFrame CreateEventTypeHandler(Type aggregateType,
            DocumentMapping aggregateMapping, MethodSlot slot)
        {
            return new MaybeDeleteFrame(slot);
        }

        internal override void validateMethod(MethodSlot method)
        {
            if (!method.Method.GetParameters().Any())
            {
                method.AddError($"ShouldDelete() requires at least one argument (the event type, the aggregate type, or IQuerySession)");
            }
        }
    }
}
