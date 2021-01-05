using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using Marten.Schema;

namespace Marten.Events.V4Concept.CodeGeneration
{
    internal class CreateMethodCollection : MethodCollection
    {
        internal override void validateMethod(MethodSlot method)
        {
            // Nothing, no special rules
        }

        public static readonly string MethodName = "Create";

        public CreateMethodCollection(Type projectionType, Type aggregateType) : base(MethodName, projectionType, aggregateType)
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
            return BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        }

        public void BuildCreateMethod(GeneratedType generatedType, DocumentMapping aggregateMapping)
        {
            var returnType = IsAsync
                ? typeof(ValueTask<>).MakeGenericType(AggregateType)
                : AggregateType;

            var args = new[] {new Argument(typeof(IEvent), "@event"), new Argument(typeof(IQuerySession), "session")};
            if (IsAsync)
            {
                args = args.Concat(new[] {new Argument(typeof(CancellationToken), "cancellation")}).ToArray();
            }

            var method = new GeneratedMethod(MethodName, returnType, args);
            generatedType.AddMethod(method);

            var frames = AddEventHandling(AggregateType, aggregateMapping, this);
            method.Frames.AddRange(frames);



            method.Frames.Add(new DefaultAggregateConstruction(AggregateType, generatedType)
                {IfStyle = Methods.Any() ? IfStyle.Else : IfStyle.None});

        }


        public override IEventHandlingFrame CreateEventTypeHandler(Type aggregateType,
            DocumentMapping aggregateMapping, MethodSlot slot)
        {
            if (slot.Method is ConstructorInfo)
            {
                return new AggregateConstructorFrame(slot);
            }

            return new CreateAggregateFrame(slot);
        }
    }
}
