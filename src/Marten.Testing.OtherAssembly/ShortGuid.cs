using System;
using System.Collections.Generic;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Marten.Schema;
using Marten.Schema.Identity;

namespace Marten.Testing.OtherAssembly;

public class ShortGuid
{
    public static string NewGuid()
    {
        return Guid.NewGuid().ToString();
    }
}

public class String2IdGeneration : IIdGeneration
{
    public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
    {
        var use = new Use(mapping.DocumentType);
        method.Frames.Code("if ({0}." + mapping.IdMember.Name + " == null) _setter({0}, global::" + typeof (ShortGuid).FullNameInCode() + ".NewGuid());", use);
        method.Frames.Code("return {0}." + mapping.IdMember.Name + ";", use);
    }

    public IEnumerable<Type> KeyTypes { get; } = new[] { typeof(string) };
    public bool RequiresSequences => false;
}
