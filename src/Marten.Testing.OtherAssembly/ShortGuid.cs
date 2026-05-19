using System;
using Marten.Schema.Identity;

namespace Marten.Testing.OtherAssembly;

public class ShortGuid
{
    public static string NewGuid()
    {
        return Guid.NewGuid().ToString();
    }
}

public class String2IdGeneration: IIdGeneration
{
    public bool IsNumeric => false;
}
