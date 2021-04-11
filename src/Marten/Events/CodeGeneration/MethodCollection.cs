using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using Baseline;
using Baseline.Reflection;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Schema;

namespace Marten.Events.CodeGeneration
{
    internal abstract class MethodCollection
    {
        private static readonly Dictionary<int, Type> _funcBaseTypes = new Dictionary<int, Type>
        {
            {1, typeof(Func<>)},
            {2, typeof(Func<,>)},
            {3, typeof(Func<,,>)},
            {4, typeof(Func<,,,>)},
            {5, typeof(Func<,,,,>)},
            {6, typeof(Func<,,,,,>)},
            {7, typeof(Func<,,,,,,>)},
            {8, typeof(Func<,,,,,,,>)},
        };

        private static readonly Dictionary<int, Type> _actionBaseTypes = new Dictionary<int, Type>
        {
            {1, typeof(Action<>)},
            {2, typeof(Action<,>)},
            {3, typeof(Action<,,>)},
            {4, typeof(Action<,,,>)},
            {5, typeof(Action<,,,,>)},
            {6, typeof(Action<,,,,,>)},
            {7, typeof(Action<,,,,,,>)},
            {8, typeof(Action<,,,,,,,>)},
        };

        private int _lambdaNumber = 0;

        internal IEnumerable<Assembly> ReferencedAssemblies()
        {
            return Methods.SelectMany(x => x.ReferencedTypes())
                .Select(x => x.Assembly)
                .Distinct();
        }

        protected virtual BindingFlags flags() => BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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

            AggregateType = aggregateType;

            projectionType.GetMethods(flags())
                .Where(x => MethodNames.Contains(x.Name))
                .Where(x => !x.HasAttribute<MartenIgnoreAttribute>())
                .Each(method => addMethodSlot(method, false));



            if (aggregateType != null)
            {
                aggregateType.GetMethods(flags())
                    .Where(x => MethodNames.Contains(x.Name))
                    .Where(x => !x.HasAttribute<MartenIgnoreAttribute>())
                    .Each(method => addMethodSlot(method, true));
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


        public string LambdaName { get; protected set; } = "Lambda";

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

        private void addMethodSlot(MethodInfo method, bool declaredByAggregate)
        {
            if (method.IsPublic)
            {
                var slot = new MethodSlot(method, AggregateType)
                {
                    HandlerType = declaredByAggregate ? AggregateType : ProjectionType,
                    DeclaredByAggregate = declaredByAggregate
                };
                Methods.Add(slot);
            }
            else
            {
                var parameterTypes = new List<Type>();
                if (declaredByAggregate)
                {
                    parameterTypes.Add(AggregateType);
                }
                else
                {
                    parameterTypes.Add(ProjectionType);
                }

                parameterTypes.AddRange(method.GetParameters().Select(x => x.ParameterType));

                var parameters = parameterTypes.Select(Expression.Parameter).ToArray();


                Type baseType = null;
                if (method.ReturnType == typeof(void))
                {
                    baseType = _actionBaseTypes[parameterTypes.Count];

                }
                else
                {
                    parameterTypes.Add(method.ReturnType);
                    baseType = _funcBaseTypes[parameterTypes.Count];
                }


                var lambdaType = baseType.MakeGenericType(parameterTypes.ToArray());
                var loaderType = typeof(LambdaLoader<>).MakeGenericType(lambdaType);
                var loader = (ILambdaLoader)Activator.CreateInstance(loaderType);
                loader.Add(this, method, AggregateType, parameters);
            }




        }

        private interface ILambdaLoader
        {
            void Add(MethodCollection methods, MethodInfo method, Type aggregateType,
                IList<ParameterExpression> parameters);
        }

        private class LambdaLoader<T> : ILambdaLoader
        {
            public void Add(MethodCollection methods, MethodInfo method, Type aggregateType, IList<ParameterExpression> parameters)
            {
                Expression body = Expression.Call(parameters[0], method, parameters.OfType<Expression>().Skip(1).ToArray());
                var expression = Expression.Lambda<T>(body, parameters);
                var lambda =  expression.Compile(); // TODO -- use FastExpressionCompiler here!

                var eventType = method.GetEventType(aggregateType);

                methods.AddLambda(lambda, eventType);
            }
        }




        public abstract IEventHandlingFrame CreateEventTypeHandler(Type aggregateType,
            DocumentMapping aggregateMapping, MethodSlot slot);

        public List<MethodSlot> Methods { get; } = new List<MethodSlot>();

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
            public int Compare(EventProcessingFrame x, EventProcessingFrame y)
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
