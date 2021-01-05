using System;
using System.Collections.Generic;
using System.Reflection;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Events.CodeGeneration
{
    internal class MaybeDeleteFrame: Frame, IEventHandlingFrame
    {
        private readonly Type _aggregateType;
        private readonly Type _identityType;
        private Variable _aggregate;
        private Variable _storage;
        private Variable _tenant;

        public MaybeDeleteFrame(Type aggregateType, Type identityType, MethodSlot slot) : base(slot.Method.As<MethodInfo>().IsAsync())
        {
            _aggregateType = aggregateType;
            _identityType = identityType;
            EventType = slot.EventType;
            Maybe = new MethodCall(slot.HandlerType, (MethodInfo) slot.Method) {Target = slot.Setter};
        }

        public MethodCall Maybe { get; }

        public Type EventType { get; }

        public void Configure(EventProcessingFrame parent)
        {
            _aggregate = parent.Aggregate;

            // Replace any arguments to Event<T>
            Maybe.TrySetArgument(parent.SpecificEvent);

            // Replace any arguments to the specific T event type
            Maybe.TrySetArgument(parent.DataOnly);
        }

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
            yield return _aggregate;

            _tenant = chain.FindVariable(typeof(ITenant));
            yield return _tenant;

            var storageType = typeof(IDocumentStorage<,>).MakeGenericType(_aggregateType, _identityType);

            _storage = chain.FindVariable(storageType);
            yield return _storage;

            foreach (var variable in Maybe.FindVariables(chain))
            {
                yield return variable;
            }
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            Maybe.GenerateCode(method, writer);
            writer.Write($"BLOCK:if ({Maybe.ReturnVariable.Usage})");

            writer.WriteLine($"return {_storage.Usage}.DeleteForDocument({_aggregate.Usage}, {_tenant.Usage});");

            writer.FinishBlock();
        }
    }
}
