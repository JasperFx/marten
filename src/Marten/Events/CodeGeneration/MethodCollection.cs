using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using LamarCodeGeneration.Util;
using Marten.Schema;

namespace Marten.Events.CodeGeneration
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

        protected virtual BindingFlags flags() => BindingFlags.Instance | BindingFlags.Public;

        public Type ProjectionType { get; }

        protected readonly List<Type> _validArgumentTypes = new List<Type>();
        protected readonly List<Type> _validReturnTypes = new List<Type>();

        internal IReadOnlyList<Type> ValidArgumentTypes => _validArgumentTypes;

        public IReadOnlyList<Type> ValidReturnTypes => _validReturnTypes;

        protected MethodCollection(string methodName, Type projectionType, Type aggregateType)
        : this(new string[]{methodName}, projectionType, aggregateType)
        {

        }

        protected MethodCollection(string[] methodNames, Type projectionType, Type aggregateType)
        {
            _validArgumentTypes.Add(typeof(CancellationToken));

            MethodNames.AddRange(methodNames);

            ProjectionType = projectionType;

            Methods = projectionType.GetMethods(flags())
                .Where(x => MethodNames.Contains(x.Name))
                .Where(x => !x.HasAttribute<MartenIgnoreAttribute>())
                .Select(x => new MethodSlot(x, aggregateType){HandlerType = projectionType}).ToList();

            AggregateType = aggregateType;

            if (aggregateType != null)
            {
                var aggregateSlots = aggregateType.GetMethods(flags())
                    .Where(x => MethodNames.Contains(x.Name))
                    .Where(x => !x.HasAttribute<MartenIgnoreAttribute>())
                    .Select(x => new MethodSlot(x, aggregateType)
                    {
                        HandlerType = aggregateType,
                        DeclaredByAggregate = true
                    });

                Methods.AddRange(aggregateSlots);
            }


            IsAsync = Methods.Select(x => x.Method).OfType<MethodInfo>().Any(x => x.IsAsync());
            LambdaName = methodNames.First();


        }

        internal static Type[] AllEventTypes(params MethodCollection[] methods)
        {
            return methods.SelectMany(x => x.EventTypes())
                .Distinct()
                .ToArray();
        }

        internal IEnumerable<Type> EventTypes()
        {
            return Methods.Where(x => x.EventType != null).Select(x => x.EventType).Distinct();
        }

        internal abstract void validateMethod(MethodSlot method);

        public Type AggregateType { get; }

        public List<string> MethodNames { get; } = new List<string>();


        public string LambdaName { get; protected set; }

        public IEnumerable<Setter> Setters()
        {
            return Methods.Where(x => x.Setter != null).Select(x => x.Setter);
        }

        public void AddLambda<T>(T lambda, Type eventType)
        {
            if (eventType == null) throw new ArgumentNullException(nameof(eventType));

            if (eventType.IsGenericType && eventType.Closes(typeof(IEvent<>)))
            {
                eventType = eventType.GetGenericArguments().Single();
            }

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

        public static EventTypePatternMatchFrame AddEventHandling(Type aggregateType, DocumentMapping mapping,
            params MethodCollection[] collections)
        {
            var byType = new Dictionary<Type, EventProcessingFrame>();

            var frames = new List<EventProcessingFrame>();

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
                        container = new EventProcessingFrame(aggregateType, frame);

                        byType.Add(frame.EventType, container);

                        frames.Add(container);
                    }

                }
            }

            frames.Sort(new EventTypeComparer());

            return new EventTypePatternMatchFrame(frames);
        }

        private class EventTypeComparer: IComparer<EventProcessingFrame>
        {
            public int Compare(EventProcessingFrame? x, EventProcessingFrame? y)
            {
                if (x.EventType.CanBeCastTo(y.EventType)) return -1;

                if (y.EventType.CanBeCastTo(x.EventType)) return 1;

                return 0;
            }
        }


        public static MethodSlot[] FindInvalidMethods(Type projectionType, params MethodCollection[] collections)
        {
            var methodNames = collections.SelectMany(x => x.MethodNames).Distinct().ToArray();

            var invalidMethods = projectionType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => !x.HasAttribute<MartenIgnoreAttribute>())
                .Where(x => x.DeclaringType.Assembly != typeof(MethodCollection).Assembly)
                .Where(x => x.DeclaringType != typeof(object))
                .Where(x => !methodNames.Contains(x.Name))
                .Select(x => MethodSlot.InvalidMethodName(x, methodNames))
                .ToList();

            foreach (var collection in collections)
            {
                // We won't validate the methods that come through inline Lambdas
                foreach (var method in collection.Methods)
                {
                    method.Validate(collection);
                    collection.validateMethod(method); // hook for unusual rules
                }

                invalidMethods.AddRange(collection.Methods.Where(x => x.Errors.Any()));
            }

            return invalidMethods.ToArray();
        }

        public bool IsEmpty()
        {
            return !Methods.Any();
        }
    }
}
