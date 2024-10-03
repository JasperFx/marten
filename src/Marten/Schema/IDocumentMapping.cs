using System;
using System.Reflection;
using Weasel.Core;

namespace Marten.Schema;

internal interface IDocumentMapping
{
    IDocumentMapping Root { get; }

    MemberInfo IdMember { get; }

    Type DocumentType { get; }

    Type IdType { get; }
    DbObjectName TableName { get; }

    public PropertySearching PropertySearching { get; }
    public DeleteStyle DeleteStyle { get; }

    /// <summary>
    /// This is a workaround for the quick append + inline projection
    /// issue
    /// </summary>
    bool UseVersionFromMatchingStream { get; set; }
}
