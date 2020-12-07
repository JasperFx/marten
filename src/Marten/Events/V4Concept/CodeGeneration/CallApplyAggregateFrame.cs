using System;
using System.Collections.Generic;
using System.Threading;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;

namespace Marten.Events.V4Concept.CodeGeneration
{
    internal class CallApplyAggregateFrame: Frame
    {
        private Variable _aggregate;
        private Variable _session;
        private Variable _cancellation;

        public CallApplyAggregateFrame(ApplyMethodCollection methods) : base(methods.IsAsync)
        {
            AggregateType = methods.AggregateType;
        }

        public Type AggregateType { get; }
        public bool InsideForEach { get; set; }

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
            _aggregate = chain.FindVariable(AggregateType);

            _session = chain.TryFindVariable(typeof(IQuerySession), VariableSource.All) ?? chain.FindVariable(typeof(IDocumentSession));
            yield return _session;

            if (IsAsync)
            {
                _cancellation = chain.TryFindVariable(typeof(CancellationToken), VariableSource.All) ??
                                new Variable(typeof(CancellationToken),
                                    $"{typeof(CancellationToken).FullNameInCode()}.None");

                yield return _cancellation;
            }
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            if (InsideForEach)
            {
                writer.Write("BLOCK:foreach (var @event in events)");
            }

            if (IsAsync)
            {
                writer.WriteLine($"{_aggregate.Usage} = await {ApplyMethodCollection.MethodName}(@event, {_aggregate.Usage}, {_session.Usage}, {_cancellation.Usage});");
            }
            else
            {
                writer.WriteLine($"{_aggregate.Usage} = {ApplyMethodCollection.MethodName}(@event, {_aggregate.Usage}, {_session.Usage});");
            }

            if (InsideForEach)
            {
                writer.FinishBlock();
            }

            Next?.GenerateCode(method, writer);
        }
    }
}
