using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using Marten.Schema;

namespace Marten.Events.V4Concept.CodeGeneration
{
    internal abstract class MethodCollection
    {
        public Type ProjectionType { get; }

        protected MethodCollection(string methodName, Type projectionType)
        {
            ProjectionType = projectionType;

            Methods = projectionType.GetMethods().Where(x => x.Name == methodName).ToArray();
            IsAsync = Methods.Any(x => x.IsAsync());
        }

        public abstract IEventHandlingFrame CreateAggregationHandler(Type aggregateType,
            DocumentMapping aggregateMapping, MethodInfo method);

        public MethodInfo[] Methods { get; }

        public bool IsAsync { get;}

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
                foreach (var methodInfo in collection.Methods)
                {
                    var frame = collection.CreateAggregationHandler(aggregateType, mapping, methodInfo);
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
