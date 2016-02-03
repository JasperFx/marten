using System;
using System.Reflection;

namespace Marten.Schema
{
    /// <summary>
    /// Base type of an Attribute that can be extended to add per field/property
    /// or per document type customization to the document storage
    /// </summary>
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


    
}