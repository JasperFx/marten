using System;

namespace Marten.Schema
{
    [AttributeUsage(AttributeTargets.Class)]
    public class PropertySearchingAttribute : MartenAttribute
    {
        private readonly PropertySearching _searching;

        public PropertySearchingAttribute(PropertySearching searching)
        {
            _searching = searching;
        }

        public override void Modify(DocumentMapping mapping)
        {
            mapping.PropertySearching = _searching;
        }
    }
}