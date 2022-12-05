using System;

namespace Marten.Schema;

/// <summary>
///     Used to alter the document type alias with Marten to
///     avoid naming collisions in the underlying Postgresql
///     schema from similarly named document
///     types
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class DocumentAliasAttribute: MartenAttribute
{
    public DocumentAliasAttribute(string alias)
    {
        Alias = alias;
    }

    public string Alias { get; }

    public override void Modify(DocumentMapping mapping)
    {
        mapping.Alias = Alias;
    }
}
