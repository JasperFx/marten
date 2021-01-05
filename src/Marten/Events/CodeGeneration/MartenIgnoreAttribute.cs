using System;

namespace Marten.Events.CodeGeneration
{
    [AttributeUsage(AttributeTargets.Method)]
    internal class MartenIgnoreAttribute: Attribute
    {

    }
}