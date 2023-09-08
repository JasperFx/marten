using System;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Core.Reflection;
using Marten.Exceptions;

namespace Marten.Events.CodeGeneration;

internal class AggregateEventProcessingFrame: EventProcessingFrame
{
    private ApplyMethodCall _apply;

    private Frame _creation;
    private ShouldDeleteFrame _deletion;

    public AggregateEventProcessingFrame(Type aggregateType, Type eventType): base(true, aggregateType, eventType)
    {
    }

    public bool AlwaysDeletes { get; set; }

    public Frame CreationFrame
    {
        get => _creation;
        set
        {
            if (value is not IEventHandlingFrame)
            {
                throw new ArgumentOutOfRangeException(
                    $"The CreationFrame must implement {nameof(IEventHandlingFrame)}");
            }

            _inner.Add(value);
            _creation = value;
        }
    }

    public ShouldDeleteFrame Deletion
    {
        get => _deletion;
        set
        {
            _deletion = value;
            _inner.Add(value);
        }
    }

    public ApplyMethodCall Apply
    {
        get => _apply;
        set
        {
            _apply = value;
            _inner.Add(value);
        }
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        writer.Write($"case {SpecificEvent.VariableType.FullNameInCode()} {SpecificEvent.Usage}:");

        writer.IndentionLevel++;


        if (AlwaysDeletes)
        {
            writer.Write("return null;");
        }

        CreationFrame?.GenerateCode(method, writer);

        if (Apply != null)
        {
            if (CreationFrame == null)
            {
                var defaultConstructor = AggregateType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes,
                    null);

                if (defaultConstructor?.IsPublic == true)
                {
                    writer.Write($"{Aggregate.Usage} ??= new {AggregateType.FullNameInCode()}();");
                }
                else if (defaultConstructor?.IsPublic == false)
                {
                    writer.Write($"{Aggregate.Usage} ??= AggregateBuilder();");
                }
                else
                {
                    var errorMessage =
                        $"Projection for {AggregateType.FullName} should either have a static Create method that returns the event type {SpecificEvent.VariableType.FullNameInCode()} or {AggregateType.FullName} should have either have a public, no argument constructor or a constructor function that takes the {SpecificEvent.VariableType.FullNameInCode()} as a parameter. This error occurs when Marten is trying to build a new aggregate, but the aggregate projection does not have a way to create a new aggregate from the first event in the event stream. A common cause is persisting events out of order according to your application's domain logic rules";

                    writer.Write(
                        $"if({Aggregate.Usage} == default) throw new {typeof(InvalidProjectionException).FullNameInCode()}(\"{errorMessage}\");");
                }
            }

            Apply.GenerateCode(method, writer);
        }

        if (Deletion != null)
        {
            writer.Write($"if ({Aggregate.Usage} == null) return null;");

            Deletion.GenerateCode(method, writer);
        }

        writer.Write($"return {Aggregate.Usage};");

        writer.IndentionLevel--;

        Next?.GenerateCode(method, writer);
    }
}
