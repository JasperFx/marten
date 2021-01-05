using System;
using System.Collections.Generic;
using System.Linq;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Events.Projections;

namespace Marten.Events.CodeGeneration
{
    internal class InitializeLiveAggregateFrame : Frame
    {
        private readonly Type _aggregateType;
        private readonly Type _idType;
        private readonly CallCreateAggregateFrame _create;
        private readonly MethodCall _loadMethod;
        private Variable _slice;
        private readonly Type _sliceType;

        public InitializeLiveAggregateFrame(Type aggregateType, Type idType, CallCreateAggregateFrame create) : base(true)
        {
            _aggregateType = aggregateType;
            _idType = idType;
            _create = create;
            _create.FirstEventExpression = "slice.Events.First()";

            _sliceType = typeof(EventSlice<,>).MakeGenericType(aggregateType, idType);

            Aggregate = new Variable(aggregateType, this);

            var load = typeof(IQuerySession)
                .GetMethods()
                .Single(x => x.Name == "LoadAsync" && x.GetParameters().First().ParameterType == idType)
                .MakeGenericMethod(aggregateType);

            _loadMethod = new MethodCall(typeof(IDocumentSession), load);
            _loadMethod.AssignResultTo(Aggregate);
        }

        public Variable Aggregate { get; }

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
            yield return Aggregate;

            _slice = chain.FindVariable(_sliceType);

            foreach (var variable in _create.FindVariables(chain))
            {
                yield return variable;
            }

            foreach (var variable in _loadMethod.FindVariables(chain))
            {
                yield return variable;
            }
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            writer.WriteLine($"{_aggregateType.FullNameInCode()} {Aggregate.Usage} = default({_aggregateType.FullNameInCode()});");
            writer.Write($"BLOCK:if ({_slice.Usage}.{nameof(StreamAction.ActionType)} == {Constant.ForEnum(StreamActionType.Start).Usage})");

            _create.Action = CreateAggregateAction.Assign;
            _create.GenerateCode(method, writer);

            writer.FinishBlock();

            writer.Write("BLOCK:else");
            _loadMethod.GenerateCode(method, writer);
            _create.Action = CreateAggregateAction.NullCoalesce;
            _create.GenerateCode(method, writer);
            writer.FinishBlock();

            Next?.GenerateCode(method, writer);
        }
    }
}
