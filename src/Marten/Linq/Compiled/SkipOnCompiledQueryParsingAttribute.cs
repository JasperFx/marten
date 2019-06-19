using System;

namespace Marten.Linq.Compiled
{
    [AttributeUsage(AttributeTargets.Method)]
    public class SkipOnCompiledQueryParsingAttribute: Attribute
    {
    }
}
