using System;
using System.Collections.Generic;
using System.Threading;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;

namespace Marten.Events.CodeGeneration;

internal class CallCreateAggregateFrame: Frame
{
    private Variable _cancellation;
    private Variable _session;

    public CallCreateAggregateFrame(CreateMethodCollection methods): base(methods.IsAsync)
    {
        Aggregate = new Variable(methods.AggregateType, this);
        UsedEventOnCreate = new Variable(typeof(bool), UsedEventOnCreateName, this);
    }

    public CallCreateAggregateFrame(CreateMethodCollection methods, Variable aggregate): base(methods.IsAsync)
    {
        Aggregate = aggregate;
    }

    public CreateAggregateAction Action { get; set; } = CreateAggregateAction.Initialize;

    public Variable Aggregate { get; private set; }

    public Variable UsedEventOnCreate { get; private set; }

    public const string UsedEventOnCreateName = "usedEventOnCreate";

    public string FirstEventExpression { get; set; } = "events[0]";

    public void CoalesceAssignTo(Variable aggregate)
    {
        Aggregate = aggregate;
        Action = CreateAggregateAction.NullCoalesce;
    }

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        _session = chain.TryFindVariable(typeof(IQuerySession), VariableSource.All) ??
                   chain.FindVariable(typeof(IDocumentSession));
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
        /*
         * Control flow:
         *
         * bool usedEventOnCreate = <true if aggregate is currently null>
         * aggregate ??= createMethod(firstEvent)
         *
         * if(<aggregate is still null, meaning no create methods match event>)
         * {
         *     usedEventOnCreate = false;
         *     // use default constructor or throw if not exists
         *     // first event passed through for exception message
         *     aggregate = createDefaultMethod(firstEvent);
         * }
         *
         * //...in call apply frame
         *
         * foreach (@event in events.Skip(<skip first event if it was used on create>))
         */

        var methodCall = IsAsync
            ? $"await {CreateMethodCollection.MethodName}({FirstEventExpression}, {_session.Usage}, {_cancellation.Usage});"
            : $"{CreateMethodCollection.MethodName}({FirstEventExpression}, {_session.Usage});";

        switch (Action)
        {
            case CreateAggregateAction.Assign:
                writer.WriteLine($"var {UsedEventOnCreate.Usage} = {Aggregate.Usage} is null;");
                writer.WriteLine($"{Aggregate.Usage} = {methodCall};");
                break;
            case CreateAggregateAction.Initialize:
                writer.WriteLine($"var {UsedEventOnCreate.Usage} = true;");
                writer.WriteLine($"var {Aggregate.Usage} = {methodCall};");
                break;
            case CreateAggregateAction.NullCoalesce:
                writer.WriteLine($"var {UsedEventOnCreate.Usage} = {Aggregate.Usage} is null;");
                writer.WriteLine($"{Aggregate.Usage} ??= {methodCall};");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(Action));
        }

        writer.Write($"BLOCK:if ({Aggregate.Usage} is null)");
        writer.WriteLine($"{UsedEventOnCreate.Usage} = false;");
        // creates default or throws if not possible
        writer.WriteLine($"{Aggregate.Usage} = {CreateDefaultMethod.MethodName}({FirstEventExpression});");
        writer.FinishBlock();

        Next?.GenerateCode(method, writer);
    }
}
