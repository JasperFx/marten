using System;

namespace Marten.Events.CodeGeneration
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
    public class MartenIgnoreAttribute: Attribute
    {

    }
}
