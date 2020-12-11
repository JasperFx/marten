using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Events.V4Concept.Aggregation;
using Marten.Schema;

namespace Marten.Events.V4Concept.CodeGeneration
{
    internal class ApplyMethodCollection : MethodCollection
    {
        public Type AggregateType { get; }
        public static readonly string MethodName = "Apply";

        public ApplyMethodCollection(Type projectionType, Type aggregateType) : base(MethodName, projectionType)
        {
            AggregateType = aggregateType;
            LambdaName = nameof(V4AggregateProjection<string>.ProjectEvent);
        }

        public override IEventHandlingFrame CreateEventTypeHandler(Type aggregateType,
            DocumentMapping aggregateMapping, MethodSlot slot)
        {
            return new ApplyMethodCall(ProjectionType, slot);
        }

        public void BuildApplyMethod(GeneratedType generatedType, DocumentMapping aggregateMapping)
        {
            var returnType = IsAsync
                ? typeof(ValueTask<>).MakeGenericType(AggregateType)
                : AggregateType;

            var args = new[]
            {
                new Argument(typeof(IEvent), "@event"),
                new Argument(AggregateType, "aggregate"),
                new Argument(typeof(IQuerySession), "session")
            };

            if (IsAsync)
            {
                args = args.Concat(new[] {new Argument(typeof(CancellationToken), "cancellation")}).ToArray();
            }

            var method = new GeneratedMethod(MethodName, returnType, args);
            generatedType.AddMethod(method);

            var frames = AddEventHandling(AggregateType, aggregateMapping, this);
            method.Frames.AddRange(frames);


            method.Frames.Code("return {0};", new Use(AggregateType));
        }

    }
}
