using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;

namespace Marten.Events.CodeGeneration
{
    public class MethodSlot
    {
        public static readonly string NoEventType = "No event type can be determined. The argument for the event should be named '@event'";

        private readonly List<string> _errors = new List<string>();

        public Setter Setter { get; }
        public MethodBase Method { get; }

        public MethodSlot(MethodInfo method, Type aggregateType)
        {
            Method = method;
            EventType = method.GetEventType(aggregateType);
            ReturnType = method.ReturnType;
            DeclaringType = method.DeclaringType;
        }

        public Type DeclaringType { get; }

        public Type ReturnType { get; }

        public Type HandlerType { get; set; }

        public Type EventType { get; }

        public MethodSlot(Setter setter, MethodInfo method, Type eventType)
        {
            Setter = setter;
            Method = method;
            EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
            DeclaringType = method.DeclaringType;
            ReturnType = method.ReturnType;
            HandlerType = setter.VariableType;
        }

        public MethodSlot(ConstructorInfo constructor, Type projectionType, Type aggregateType)
        {
            if (constructor.GetParameters().Length > 1)
            {
                throw new ArgumentOutOfRangeException($"Only single argument constructor functions can be used here.");
            }

            var parameterType = constructor.GetParameters().Single().ParameterType;
            EventType = parameterType.Closes(typeof(Event<>))
                ? parameterType.GetGenericArguments().Single()
                : parameterType;

            ReturnType = aggregateType;
            HandlerType = projectionType;
            DeclaringType = aggregateType;
            Method = constructor;
        }

        public IEnumerable<Type> ReferencedTypes()
        {
            yield return DeclaringType;
            yield return EventType;
        }

        public string Signature()
        {
            var description = $"{Method.Name}({Method.GetParameters().Select(x => x.ParameterType.NameInCode()).Join(", ")})";
            if (ReturnType != typeof(void))
            {
                description += $" : {ReturnType.NameInCode()}";
            }

            return description;
        }

        public IReadOnlyList<string> Errors => _errors;
        public bool DeclaredByAggregate { get; set; }

        internal void Validate(MethodCollection collection)
        {
            if (EventType == null)
            {
                _errors.Add(NoEventType);
            }
            else
            {
                validateArguments(collection);
            }

            if (collection.ValidReturnTypes.Any() && !collection.ValidReturnTypes.Contains(ReturnType))
            {
                var message = $"Return type '{ReturnType.FullNameInCode()}' is invalid. The valid options are {collection.ValidArgumentTypes.Select(x => x.FullNameInCode()).Join(", ")}";
                AddError(message);
            }
        }

        internal void AddError(string error)
        {
            _errors.Add(error);
        }

        private void validateArguments(MethodCollection collection)
        {
            var possibleTypes = new List<Type>(collection.ValidArgumentTypes) {EventType};
            if (EventType != null)
            {
                possibleTypes.Add(typeof(Event<>).MakeGenericType(EventType));
            }
            if (collection.AggregateType != null)
            {
                possibleTypes.Fill(collection.AggregateType);
            }

            foreach (var parameter in Method.GetParameters())
            {
                var type = parameter.ParameterType;
                if (!possibleTypes.Contains(type))
                {
                    _errors.Add(
                        $"Parameter of type '{type.FullNameInCode()}' is not supported. Valid options are {possibleTypes.Select(x => x.FullNameInCode()).Join(", ")}");
                }
            }
        }

        public static MethodSlot InvalidMethodName(MethodInfo methodInfo, string[] methodNames)
        {
            var slot = new MethodSlot(methodInfo, null);
            slot._errors.Add($"Unrecognized method name '{methodInfo.Name}'. Either mark with [MartenIgnore] or use one of {methodNames.Select(x => $"'{x}'").Join(", ")}");

            return slot;
        }
    }
}
