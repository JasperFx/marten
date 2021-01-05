using System;
using System.Reflection;
using LamarCodeGeneration.Frames;

namespace Marten.Events.CodeGeneration
{
    /// <summary>
    ///     Calls an AggregatedProjection.Create() method
    /// </summary>
    internal class CreateAggregateFrame: MethodCall, IEventHandlingFrame
    {
        public CreateAggregateFrame(Type handlerType, string methodName, Type aggregateType): base(handlerType, methodName)
        {
            ReturnAction = ReturnAction.Return;
            EventType = Method.GetEventType(aggregateType);
        }

        public CreateAggregateFrame(MethodSlot slot): base(slot.HandlerType, (MethodInfo) slot.Method)
        {
            ReturnAction = ReturnAction.Return;
            EventType = slot.EventType;
            if (slot.Setter != null)
            {
                Target = slot.Setter;
            }
        }

        public Type EventType { get;}

        public void Configure(EventProcessingFrame parent)
        {
            // Replace any arguments to Event<T>
            TrySetArgument(parent.SpecificEvent);

            // Replace any arguments to the specific T event type
            TrySetArgument(parent.DataOnly);
        }
    }
}
