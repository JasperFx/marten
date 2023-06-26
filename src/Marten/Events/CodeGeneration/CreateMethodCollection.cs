using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using Marten.Schema;

namespace Marten.Events.CodeGeneration;

internal class CreateMethodCollection: MethodCollection
{
    public static readonly string MethodName = "Create";

    public CreateMethodCollection(Type projectionType, Type aggregateType): base(MethodName, projectionType,
        aggregateType)
    {
        _validArgumentTypes.Add(typeof(IQuerySession));

        _validReturnTypes.Fill(aggregateType);
        _validReturnTypes.Add(typeof(Task<>).MakeGenericType(aggregateType));

        var constructors = aggregateType
            .GetConstructors()
            .Where(x => x.GetParameters().Length == 1 && x.GetParameters().Single().ParameterType.IsClass);

        foreach (var constructor in constructors)
        {
            var slot = new MethodSlot(constructor, projectionType, aggregateType);
            Methods.Add(slot);
        }
    }

    internal override void validateMethod(MethodSlot method)
    {
        // Nothing, no special rules
    }

    protected override BindingFlags flags()
    {
        return BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    }

    public void BuildCreateMethod(GeneratedType generatedType, IDocumentMapping aggregateMapping)
    {
        var returnType = IsAsync
            ? typeof(ValueTask<>).MakeGenericType(AggregateType)
            : AggregateType;

        var args = new[] { new Argument(typeof(IEvent), "@event"), new Argument(typeof(IQuerySession), "session") };
        if (IsAsync)
        {
            args = args.Concat(new[] { new Argument(typeof(CancellationToken), "cancellation") }).ToArray();
        }

        var method = new GeneratedMethod(MethodName, returnType, args);
        method.AsyncMode = IsAsync ? AsyncMode.AsyncTask : AsyncMode.None;
        generatedType.AddMethod(method);

        var eventHandling = AddEventHandling(AggregateType, aggregateMapping, this);
        method.Frames.Add(eventHandling);

        method.Frames.ReturnNull();
    }

    public override IEventHandlingFrame CreateEventTypeHandler(Type aggregateType,
        IDocumentMapping aggregateMapping, MethodSlot slot)
    {
        if (slot.Method is ConstructorInfo)
        {
            return new AggregateConstructorFrame(slot);
        }

        return new CreateAggregateFrame(slot);
    }
}
