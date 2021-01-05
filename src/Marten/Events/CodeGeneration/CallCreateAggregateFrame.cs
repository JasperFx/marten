using System;
using System.Collections.Generic;
using System.Threading;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;

namespace Marten.Events.CodeGeneration
{
    public enum CreateAggregateAction
    {
        Initialize,
        Assign,
        NullCoalesce // TODO -- this should be in Lamar itself
    }



    // TODO -- LamarCodeGeneration needs a "call generated method" Frame
    internal class CallCreateAggregateFrame: Frame
    {
        private Variable _session;
        private Variable _cancellation;

        public CallCreateAggregateFrame(CreateMethodCollection methods) : base(methods.IsAsync)
        {
            Aggregate = new Variable(methods.AggregateType, this);
        }

        public CallCreateAggregateFrame(CreateMethodCollection methods, Variable aggregate) : base(methods.IsAsync)
        {
            Aggregate = aggregate;
        }

        public void CoalesceAssignTo(Variable aggregate)
        {
            Aggregate = aggregate;
            Action = CreateAggregateAction.NullCoalesce;
        }

        public CreateAggregateAction Action { get; set; } = CreateAggregateAction.Initialize;

        public Variable Aggregate { get; private set; }

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
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

        public string FirstEventExpression { get; set; } = "events[0]";

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            string declaration = null;
            switch (Action)
            {
                case CreateAggregateAction.Assign:
                    declaration = $"{Aggregate.Usage} =";
                    break;
                case CreateAggregateAction.Initialize:
                    declaration = $"var {Aggregate.Usage} =";
                    break;
                case CreateAggregateAction.NullCoalesce:
                    declaration = $"{Aggregate.Usage} ??=";
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(Action));
            }

            if (IsAsync)
            {
                writer.WriteLine($"{declaration} await {CreateMethodCollection.MethodName}({FirstEventExpression}, {_session.Usage}, {_cancellation.Usage});");
            }
            else
            {
                writer.WriteLine($"{declaration} {CreateMethodCollection.MethodName}({FirstEventExpression}, {_session.Usage});");
            }

            Next?.GenerateCode(method, writer);
        }
    }
}
