using System;
using System.Linq;
using System.Reflection;

namespace Marten.Util;

// TODO -- move this to JasperFx.CodeGeneration itself
public static class CodeGenExtensions
{
    public static Type FindPreGeneratedType(this Assembly assembly, string @namespace, string typeName)
    {
        var fullName = $"{@namespace}.{typeName}";
        return assembly.ExportedTypes.FirstOrDefault(x => x.FullName == fullName);
    }

    // TODO -- need a GenerationRules.CloneForParent(ICodeFileCollection)
    // TODO -- GenerationRules needs a quick "FindProjectPath()" function
}
