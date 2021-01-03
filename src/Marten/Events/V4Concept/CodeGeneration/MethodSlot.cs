using System;
using System.Collections.Generic;
using System.Reflection;
using LamarCodeGeneration.Model;

namespace Marten.Events.V4Concept.CodeGeneration
{
    internal class MethodSlot
    {
        public Setter Setter { get; }
        public MethodInfo Method { get; }

        public MethodSlot(MethodInfo method, Type aggregateType)
        {
            Method = method;
            EventType = method.GetEventType(aggregateType);
        }

        public Type HandlerType { get; set; }

        public Type EventType { get; }

        public MethodSlot(Setter setter, MethodInfo method, Type eventType)
        {
            Setter = setter;
            Method = method;
            EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
        }

        public IEnumerable<Type> ReferencedTypes()
        {
            yield return Method.DeclaringType;
            yield return EventType;
        }
    }
}
