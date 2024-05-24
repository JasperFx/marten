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

        if (Apply == null)
        {
            if (CreationFrame != null)
            {
                CreationFrame.GenerateCode(method, writer);
            }
            else
            {
                writer.Write($"{Aggregate.Usage} ??= CreateDefault(evt);");
            }
        }
        else if (CreationFrame != null)
        {
            writer.Write($"BLOCK:if ({Aggregate.Usage} == null)");

            CreationFrame.GenerateCode(method, writer);

            writer.FinishBlock();
            writer.WriteElse();
            Apply.GenerateCode(method, writer);
            writer.FinishBlock();
        }
        else // Have an Apply() method and no Create()
        {
            writer.Write($"{Aggregate.Usage} ??= CreateDefault(evt);");
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
