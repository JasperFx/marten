using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using LamarCodeGeneration.Util;
using Marten.Schema;

namespace Marten.Events.V4Concept.CodeGeneration
{
    internal abstract class MethodCollection
    {
        private int _lambdaNumber = 0;

        internal IEnumerable<Assembly> ReferencedAssemblies()
        {
            return Methods.SelectMany(x => x.ReferencedTypes())
                .Select(x => x.Assembly)
                .Distinct();
        }

        public Type ProjectionType { get; }

        protected MethodCollection(string methodName, Type projectionType, Type aggregateType)
        : this(new string[]{methodName}, projectionType, aggregateType)
        {

        }

        protected MethodCollection(string[] methodNames, Type projectionType, Type aggregateType)
        {
            MethodNames.AddRange(methodNames);

            ProjectionType = projectionType;

            Methods = projectionType.GetMethods()
                .Where(x => MethodNames.Contains(x.Name))
                .Where(x => !x.HasAttribute<IgnoreProjectionMethodAttribute>())
                .Select(x => new MethodSlot(x, aggregateType){HandlerType = projectionType}).ToList();

            if (aggregateType != null)
            {
                var aggregateSlots = aggregateType.GetMethods()
                    .Where(x => MethodNames.Contains(x.Name))
                    .Where(x => !x.HasAttribute<IgnoreProjectionMethodAttribute>())
                    .Select(x => new MethodSlot(x, aggregateType){HandlerType = aggregateType});

                Methods.AddRange(aggregateSlots);
            }


            IsAsync = Methods.Any(x => x.Method.IsAsync());
            LambdaName = methodNames.First();
        }

        public List<string> MethodNames { get; } = new List<string>();


        public string LambdaName { get; protected set; }

        public IEnumerable<Setter> Setters()
        {
            return Methods.Where(x => x.Setter != null).Select(x => x.Setter);
        }

        public void AddLambda<T>(T lambda, Type eventType)
        {
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));

            var name = LambdaName + (++_lambdaNumber).ToString();
            var method = lambda.GetType().GetMethod("Invoke");
            var setter = new Setter(typeof(T), name){InitialValue = lambda};
            var slot = new MethodSlot(setter, method, eventType);
            Methods.Add(slot);

            if (method.IsAsync())
            {
                IsAsync = true;
            }
        }

        public abstract IEventHandlingFrame CreateEventTypeHandler(Type aggregateType,
            DocumentMapping aggregateMapping, MethodSlot slot);

        public List<MethodSlot> Methods { get; }

        public bool IsAsync { get; private set; }

        public static IList<Frame> AddEventHandling(Type aggregateType, DocumentMapping mapping,
            params MethodCollection[] collections)
        {
            var byType = new Dictionary<Type, EventProcessingFrame>();

            // TODO -- later we'll worry about abstract/interface applications
            // of events

            var frames = new List<Frame>();

            var ifStyle = IfStyle.If;

            foreach (var collection in collections)
            {
                foreach (var slot in collection.Methods)
                {
                    var frame = collection.CreateEventTypeHandler(aggregateType, mapping, slot);
                    if (byType.TryGetValue(frame.EventType, out var container))
                    {
                        container.Add((Frame) frame);
                    }
                    else
                    {
                        container = new EventProcessingFrame(aggregateType, frame)
                        {
                            IfStyle = ifStyle
                        };

                        ifStyle = IfStyle.ElseIf;

                        byType.Add(frame.EventType, container);

                        frames.Add(container);
                    }

                }
            }

            return frames;
        }
    }
}
