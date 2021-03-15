using System;
using System.Linq;
using System.Reflection;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using Marten.Util;

namespace Marten.Events.CodeGeneration
{
    internal class AggregateEventProcessingFrame: EventProcessingFrame
    {
        public AggregateEventProcessingFrame(Type aggregateType, Type eventType) : base(true, aggregateType, eventType)
        {
        }

        public bool AlwaysDeletes { get; set; }

        private Frame _creation;
        private MaybeDeleteFrame _deletion;
        private ApplyMethodCall _apply;

        public Frame CreationFrame
        {
            get
            {
                return _creation;
            }
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

        public MaybeDeleteFrame Deletion
        {
            get
            {
                return _deletion;
            }
            set
            {
                _deletion = value;
                _inner.Add(value);
            }
        }

        public ApplyMethodCall Apply
        {
            get
            {
                return _apply;
            }
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
                        var errorMessage = $"Projection for {AggregateType.FullName} should either have the Create Method or Constructor for event of type {SpecificEvent.VariableType.FullName}, or {AggregateType.FullName} should have a Default Constructor.";

                        writer.Write($"if({Aggregate.Usage} == default) throw new ArgumentException(\"{errorMessage}\");");
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
}
