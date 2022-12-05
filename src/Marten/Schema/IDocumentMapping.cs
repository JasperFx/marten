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
}
