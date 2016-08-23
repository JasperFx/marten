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
}