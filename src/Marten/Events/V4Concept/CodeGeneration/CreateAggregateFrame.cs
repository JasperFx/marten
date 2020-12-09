using System;
using System.Reflection;
using LamarCodeGeneration.Frames;

namespace Marten.Events.V4Concept.CodeGeneration
{
    /// <summary>
    ///     Calls an AggregatedProjection.Create() method
    /// </summary>
    internal class CreateAggregateFrame: MethodCall, IEventHandlingFrame
    {
        public CreateAggregateFrame(Type handlerType, string methodName): base(handlerType, methodName)
        {
            ReturnAction = ReturnAction.Return;
            EventType = Method.GetEventType();
        }

        public CreateAggregateFrame(Type handlerType, MethodInfo method): base(handlerType, method)
        {
            ReturnAction = ReturnAction.Return;
            EventType = Method.GetEventType();
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
