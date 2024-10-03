using System;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.Events.CodeGeneration;

public static class CodeGenerationExtensions
{
    public static Type GetEventType(this MethodInfo method, Type aggregateType)
    {
        var candidate = method.GetParameters().Where(x => x.ParameterType.Closes(typeof(IEvent<>)));
        if (candidate.Count() == 1)
        {
            return candidate.Single().ParameterType.GetGenericArguments()[0];
        }

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
        {
            return parameterInfo.ParameterType.GetGenericArguments()[0];
        }


        return parameterInfo.ParameterType;
    }

    public static void AppendSql(this FramesCollection collection, string sql)
    {
        collection.Code($"{{0}}.{nameof(CommandBuilder.Append)}(\"{sql}\");", Use.Type<ICommandBuilder>());
    }

    public static void AppendSql(this FramesCollection collection, char sql)
    {
        collection.Code($"{{0}}.{nameof(CommandBuilder.Append)}('{sql}');", Use.Type<ICommandBuilder>());
    }
}
