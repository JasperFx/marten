using System;

namespace Marten.Schema
{
    /// <summary>
    /// Used to alter the document type alias with Marten to
    /// avoid naming collisions in the underlying Postgresql
    /// schema from similarly named document
    /// types
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class DocumentAliasAttribute: MartenAttribute
    {
        private readonly string _alias;

        public DocumentAliasAttribute(string alias)
        {
            _alias = alias;
        }

        public override void Modify(DocumentMapping mapping)
        {
            mapping.Alias = _alias;
        }

        public string Alias => _alias;
    }
}
