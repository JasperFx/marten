using System;
using System.Reflection;
#nullable enable
namespace Marten.Schema
{
    /// <summary>
    /// Adds a gin index to the JSONB data of a document
    /// </summary>
    #region sample_GinIndexedAttribute
    [AttributeUsage(AttributeTargets.Class)]
    public class GinIndexedAttribute: MartenAttribute
    {
        public override void Modify(DocumentMapping mapping)
        {
            mapping.AddGinIndexToData();
        }
    }

    #endregion sample_GinIndexedAttribute
}
