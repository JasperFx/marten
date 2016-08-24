using System;

namespace Marten.Schema
{
    /// <summary>
    /// Disables the database GRANT's for deletion if applied
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class CannotDeleteAttribute : MartenAttribute
    {
        public override void Modify(DocumentMapping mapping)
        {
            mapping.Deletions = Deletions.CannotDelete;
        }
    }

    /// <summary>
    /// Allows you to duplicate storage with other classes of the same name
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class StructuralTypedAttribute : MartenAttribute
    {
        public override void Modify(DocumentMapping mapping)
        {
            mapping.StructuralTyped = true;
        }
    }
}