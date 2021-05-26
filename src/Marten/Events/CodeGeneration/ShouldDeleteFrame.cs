using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Events.CodeGeneration
{
    internal class ShouldDeleteFrame: Frame, IEventHandlingFrame
    {
        private static int _number = 0;

        private Variable _aggregate;

        public ShouldDeleteFrame(MethodSlot slot) : base(slot.Method.As<MethodInfo>().IsAsync())
        {
            EventType = slot.EventType;
            Maybe = new MethodCall(slot.HandlerType, (MethodInfo) slot.Method) {Target = slot.Setter};
            Maybe.ReturnVariable.OverrideName(Maybe.ReturnVariable.Usage + ++_number);
        }

        public MethodCall Maybe { get; }

        public Type EventType { get; }

        public void Configure(EventProcessingFrame parent)
        {
            _aggregate = parent.Aggregate;

            // Replace any arguments to IEvent<T>

            if (Maybe.Method.GetParameters().Any(x => x.ParameterType == parent.SpecificEvent.VariableType))
            {
                // TODO -- there's a LamarCodeGeneration bug here. It's using CanCastTo(), but should be using an exact match,
                // or looser find of the argument
                Maybe.TrySetArgument(parent.SpecificEvent);
            }

            // Replace any arguments to the specific T event type
            Maybe.TrySetArgument(parent.DataOnly);
        }

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
            yield return _aggregate;

            foreach (var variable in Maybe.FindVariables(chain))
            {
                yield return variable;
            }
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            Maybe.GenerateCode(method, writer);
            writer.Write($"BLOCK:if ({Maybe.ReturnVariable.Usage})");

            writer.WriteLine($"return null;");

            writer.FinishBlock();
        }
    }
}
