using System;
using System.Reflection;

namespace Marten.Schema
{
    // SAMPLE: MartenAttribute
    public abstract class MartenAttribute : Attribute
    {
        /// <summary>
        /// Customize Document storage at the document level
        /// </summary>
        /// <param name="mapping"></param>
        public virtual void Modify(DocumentMapping mapping) { }

        /// <summary>
        /// Customize the Document storage for a single member
        /// </summary>
        /// <param name="mapping"></param>
        /// <param name="member"></param>
        public virtual void Modify(DocumentMapping mapping, MemberInfo member) { }
    }
    // ENDSAMPLE


    /// <summary>
    /// Adds a gin index to the JSONB data of a document
    /// </summary>
    // SAMPLE: GinIndexedAttribute
    public class GinIndexedAttribute : MartenAttribute
    {
        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            mapping.AddGinIndexToData();
        }
    }
    // ENDSAMPLE
}