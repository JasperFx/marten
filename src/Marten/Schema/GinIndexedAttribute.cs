using System;
using System.Reflection;

namespace Marten.Schema
{
    /// <summary>
    /// Adds a gin index to the JSONB data of a document
    /// </summary>
    // SAMPLE: GinIndexedAttribute
    [AttributeUsage(AttributeTargets.Class)]
    public class GinIndexedAttribute: MartenAttribute
    {
        public override void Modify(DocumentMapping mapping)
        {
            mapping.AddGinIndexToData();
        }
    }

    // ENDSAMPLE
}
