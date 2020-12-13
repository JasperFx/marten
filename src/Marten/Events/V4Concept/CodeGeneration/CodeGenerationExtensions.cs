using System;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Internal;

namespace Marten.Events.V4Concept.CodeGeneration
{
    public static class CodeGenerationExtensions
    {
        public static Type GetEventType(this MethodInfo method, Type aggregateType)
        {
            var parameterInfo = method.GetParameters().FirstOrDefault(x => x.Name == "@event" || x.Name == "event");
            if (parameterInfo == null)
            {
                var candidates = method
                    .GetParameters()
                    .Where(x => x.ParameterType.Assembly != typeof(IMartenSession).Assembly)
                    .Where(x => x.ParameterType != aggregateType).ToList();

                if (candidates.Count == 1)
                {
                    parameterInfo = candidates.Single();
                }
                else
                {
                    return null;
                }
            }

            if (parameterInfo.ParameterType.Closes(typeof(Event<>)))
                return parameterInfo.ParameterType.GetGenericArguments()[0];



            return parameterInfo.ParameterType;
        }
    }
}
