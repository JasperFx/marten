using System;
using Marten.Linq.QueryHandlers;
using Marten.Util;

namespace Marten.Services
{
    public interface IStorageOperation : IQueryHandler
    {
        Type DocumentType { get; }
    }
}