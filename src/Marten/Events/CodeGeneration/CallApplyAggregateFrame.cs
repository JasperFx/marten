using System;
using System.Collections.Generic;
using System.Threading;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core.Reflection;

namespace Marten.Events.CodeGeneration;

internal class CallApplyAggregateFrame: Frame
{
    private readonly Type _projectionType;
    private Variable _aggregate;
    private Variable _cancellation;
    private Variable _session;
    private Variable _usedEventOnCreate;
    private Variable _projection;

    public CallApplyAggregateFrame(ApplyMethodCollection methods, Type projectionType): base(methods.IsAsync)
    {
        _projectionType = projectionType;
        AggregateType = methods.AggregateType;
    }

    public Type AggregateType { get; }
    public bool InsideForEach { get; set; }

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        _aggregate = chain.FindVariable(AggregateType);

        _session = chain.TryFindVariable(typeof(IQuerySession), VariableSource.All) ??
                   chain.FindVariable(typeof(IDocumentSession));

        _projection = chain.FindVariable(_projectionType);
        yield return _projection;

        _usedEventOnCreate = chain.FindVariableByName(typeof(bool), CallCreateAggregateFrame.UsedEventOnCreateName);

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
            writer.Write($"BLOCK:foreach (var @event in events.Skip({_usedEventOnCreate.Usage} ? 1 : 0))");
        }

        if (IsAsync)
        {
            writer.WriteLine(
                $"{_aggregate.Usage} = await {ApplyMethodCollection.MethodName}(@event, {_aggregate.Usage}, {_session.Usage}, {_cancellation.Usage});");
        }
        else
        {
            writer.WriteLine(
                $"{_aggregate.Usage} = {ApplyMethodCollection.MethodName}(@event, {_aggregate.Usage}, {_session.Usage});");
        }

        writer.WriteLine($"if ({_aggregate.Usage} != null) {_projection.Usage}.ApplyMetadata({_aggregate.Usage}, @event);");

        if (InsideForEach)
        {
            writer.FinishBlock();
        }

        Next?.GenerateCode(method, writer);
    }
}
