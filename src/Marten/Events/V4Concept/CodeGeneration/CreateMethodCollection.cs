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
        protected override void validateMethod(MethodSlot method)
        {
            // Nothing, no special rules
        }

        public static readonly string MethodName = "Create";

        public CreateMethodCollection(Type projectionType, Type aggregateType) : base(MethodName, projectionType, aggregateType)
        {
            _validArgumentTypes.Add(typeof(IQuerySession));

            _validReturnTypes.Fill(aggregateType);
            _validReturnTypes.Add(typeof(Task<>).MakeGenericType(aggregateType));
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
            return new CreateAggregateFrame(slot);
        }
    }
}
