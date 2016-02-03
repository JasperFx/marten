using System.Reflection;

namespace Marten.Schema
{
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