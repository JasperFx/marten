using System;

namespace Marten.Events.CodeGeneration
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
    internal class MartenIgnoreAttribute: Attribute
    {

    }
}
