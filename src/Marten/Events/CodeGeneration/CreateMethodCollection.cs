using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using Marten.Schema;

namespace Marten.Events.CodeGeneration
{
    internal class CreateMethodCollection: MethodCollection
    {
        internal override void validateMethod(MethodSlot method)
        {
            // Nothing, no special rules
        }

        public static readonly string MethodName = "Create";

        public CreateMethodCollection(Type projectionType, Type aggregateType): base(MethodName, projectionType,
            aggregateType)
        {
            _validArgumentTypes.Add(typeof(IQuerySession));

            _validReturnTypes.Fill(aggregateType);
            _validReturnTypes.Add(typeof(Task<>).MakeGenericType(aggregateType));

            var constructors = aggregateType
                .GetConstructors()
                .Where(x => x.GetParameters().Length == 1);

            foreach (var constructor in constructors)
            {
                var slot = new MethodSlot(constructor, projectionType, aggregateType);
                Methods.Add(slot);
            }
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
}
