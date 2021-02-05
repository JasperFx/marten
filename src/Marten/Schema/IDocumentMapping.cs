using System;
using Marten.Schema.Identity;
using Marten.Storage;

namespace Marten.Schema
{
    public interface IDocumentMapping
    {
        IDocumentMapping Root { get; }

        Type DocumentType { get; }

        DbObjectName TableName { get; }

        Type IdType { get; }
    }

}
