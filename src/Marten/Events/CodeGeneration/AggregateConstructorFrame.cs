using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;

namespace Marten.Events.CodeGeneration;

/// <summary>
///     Calls the aggregate's single argument constructor for a specific
///     event type
/// </summary>
internal class AggregateConstructorFrame: SyncFrame, IEventHandlingFrame
{
    private readonly Type _argType;
    private readonly MethodSlot _slot;
    private Variable _aggregate;
    private Variable _arg;

    public AggregateConstructorFrame(MethodSlot slot)
    {
        _slot = slot;
        _argType = slot.Method.GetParameters().Single().ParameterType;
    }

    public void Configure(EventProcessingFrame parent)
    {
        _arg = parent.SpecificEvent.VariableType == _argType ? parent.SpecificEvent : parent.DataOnly;
        _aggregate = parent.Aggregate;
    }

    public Type EventType => _slot.EventType;

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        if (_aggregate != null)
        {
            yield return _aggregate;
        }

        yield return _arg;
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        if (_aggregate == null)
        {
            writer.WriteLine($"return new {_slot.ReturnType.FullNameInCode()}({_arg.Usage});");
        }
        else
        {
            writer.WriteLine($"{_aggregate.Usage} ??= new {_slot.ReturnType.FullNameInCode()}({_arg.Usage});");
        }


        Next?.GenerateCode(method, writer);
    }
}
