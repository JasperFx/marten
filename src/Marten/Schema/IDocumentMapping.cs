using System;
using System.Reflection;
using JasperFx.Events.CodeGeneration;
using Weasel.Core;

namespace Marten.Schema;

internal interface IDocumentMapping : IStorageMapping
{
    IDocumentMapping Root { get; }

    MemberInfo IdMember { get; }

    Type DocumentType { get; }

    Type IdType { get; }
    DbObjectName TableName { get; }

    public PropertySearching PropertySearching { get; }
    public DeleteStyle DeleteStyle { get; }
}
