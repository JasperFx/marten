using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using Marten.Events.Aggregation;
using Marten.Schema;

namespace Marten.Events.CodeGeneration;

internal class ApplyMethodCollection: MethodCollection
{
    public static readonly string MethodName = "Apply";

    public ApplyMethodCollection(Type projectionType, Type aggregateType): base(MethodName, projectionType,
        aggregateType)
    {
        LambdaName = nameof(SingleStreamAggregation<string>.ProjectEvent);
        _validArgumentTypes.Add(typeof(IQuerySession));
        _validArgumentTypes.Add(aggregateType);

        _validReturnTypes.Add(typeof(Task));
        _validReturnTypes.Add(typeof(void));
        _validReturnTypes.Add(aggregateType);
        _validReturnTypes.Add(typeof(Task<>).MakeGenericType(aggregateType));
    }

    internal override void validateMethod(MethodSlot method)
    {
        if (!method.DeclaredByAggregate && method.Method.GetParameters().All(x => x.ParameterType != AggregateType))
        {
            method.AddError($"Aggregate type '{AggregateType.FullNameInCode()}' is required as a parameter");
        }
    }

    public override IEventHandlingFrame CreateEventTypeHandler(Type aggregateType,
        IDocumentMapping aggregateMapping, MethodSlot slot)
    {
        return new ApplyMethodCall(slot);
    }

    public void BuildApplyMethod(GeneratedType generatedType, IDocumentMapping aggregateMapping)
    {
        var returnType = IsAsync
            ? typeof(ValueTask<>).MakeGenericType(AggregateType)
            : AggregateType;

        var args = new[]
        {
            new Argument(typeof(IEvent), "@event"), new Argument(AggregateType, "aggregate"),
            new Argument(typeof(IQuerySession), "session")
        };

        if (IsAsync)
        {
            args = args.Concat(new[] { new Argument(typeof(CancellationToken), "cancellation") }).ToArray();
        }

        var method = new GeneratedMethod(MethodName, returnType, args);
        generatedType.AddMethod(method);

        var eventHandling = AddEventHandling(AggregateType, aggregateMapping, this);
        method.Frames.Add(eventHandling);


        method.Frames.Code("return {0};", new Use(AggregateType));
    }
}
