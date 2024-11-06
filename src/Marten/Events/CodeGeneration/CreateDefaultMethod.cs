using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Schema;

namespace Marten.Events.CodeGeneration;

internal class CreateDefaultMethod: MethodCollection
{
    public static readonly string MethodName = "CreateDefault";

    public CreateDefaultMethod(Type projectionType, Type aggregateType) : base(MethodName, projectionType,
        aggregateType)
    {
        _validReturnTypes.Fill(aggregateType);
    }

    internal override void validateMethod(MethodSlot method)
    {
        // Nothing, no special rules
    }

    protected override BindingFlags flags()
    {
        return BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    }

    public void BuildCreateDefaultMethod(GeneratedType generatedType, IDocumentMapping aggregateMapping)
    {
        var args = new[] { new Argument(typeof(IEvent), "@event") };
        var method = new GeneratedMethod(MethodName, AggregateType, args);
        method.AsyncMode = AsyncMode.None;
        generatedType.AddMethod(method);
        method.Frames.Add(new DefaultAggregateConstruction(AggregateType, generatedType)
        {
            IfStyle = IfStyle.None,
            AdditionalNoConstructorExceptionDetails =
                " or Create method for {@event.DotNetTypeName} event type." +
                "Check more about the create method convention in documentation: https://martendb.io/events/projections/event-projections.html#create-method-convention. " +
                "If you're using Upcasting, check if {@event.DotNetTypeName} is an old event type. " +
                "If it is, make sure to define transformation for it to new event type. " +
                "Read more in Upcasting docs: https://martendb.io/events/versioning.html#upcasting-advanced-payload-transformations"
        });
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
