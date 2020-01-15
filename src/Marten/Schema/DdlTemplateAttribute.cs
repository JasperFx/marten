using System;

namespace Marten.Schema
{
    /// <summary>
    /// Override the DDL template for a single document type
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class DdlTemplateAttribute: MartenAttribute
    {
        private readonly string _templateName;

        public DdlTemplateAttribute(string templateName)
        {
            _templateName = templateName;
        }

        public override void Modify(DocumentMapping mapping)
        {
            mapping.DdlTemplate = _templateName;
        }
    }
}
