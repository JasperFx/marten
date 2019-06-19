using System;

namespace Marten.Schema
{
    /// <summary>
    /// Allows you to duplicate storage with other classes of the same name
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class StructuralTypedAttribute: MartenAttribute
    {
        public override void Modify(DocumentMapping mapping)
        {
            mapping.StructuralTyped = true;
        }
    }
}
