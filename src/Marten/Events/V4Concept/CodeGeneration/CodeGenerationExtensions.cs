using System;
using System.Linq;
using System.Reflection;
using Baseline;

namespace Marten.Events.V4Concept.CodeGeneration
{
    public static class CodeGenerationExtensions
    {
        public static Type GetEventType(this MethodInfo method)
        {
            var parameterInfo = method.GetParameters().FirstOrDefault(x => x.Name == "@event" || x.Name == "event");
            if (parameterInfo == null) return null;

            if (parameterInfo.ParameterType.Closes(typeof(Event<>)))
                return parameterInfo.ParameterType.GetGenericArguments()[0];

            return parameterInfo.ParameterType;
        }
    }
}
