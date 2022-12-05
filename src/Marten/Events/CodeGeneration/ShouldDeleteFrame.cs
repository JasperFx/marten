using System;
using System.Collections.Generic;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core.Reflection;

namespace Marten.Events.CodeGeneration;

internal class ShouldDeleteFrame: Frame, IEventHandlingFrame
{
    private static int _number;

    private Variable _aggregate;

    public ShouldDeleteFrame(MethodSlot slot): base(slot.Method.As<MethodInfo>().IsAsync())
    {
        EventType = slot.EventType;
        Maybe = new MethodCall(slot.HandlerType, (MethodInfo)slot.Method) { Target = slot.Setter };
        Maybe.ReturnVariable.OverrideName(Maybe.ReturnVariable.Usage + ++_number);
    }

    public MethodCall Maybe { get; }

    public Type EventType { get; }

    public void Configure(EventProcessingFrame parent)
    {
        _aggregate = parent.Aggregate;

        // Replace any arguments to IEvent<T>
        Maybe.TrySetArgument(parent.SpecificEvent);

        // Replace any arguments to the specific T event type
        Maybe.TrySetArgument(parent.DataOnly);
    }

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        yield return _aggregate;

        foreach (var variable in Maybe.FindVariables(chain)) yield return variable;
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        Maybe.GenerateCode(method, writer);
        writer.Write($"BLOCK:if ({Maybe.ReturnVariable.Usage})");

        writer.WriteLine("return null;");

        writer.FinishBlock();
    }
}
