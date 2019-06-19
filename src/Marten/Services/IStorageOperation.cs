using System;
using Marten.Linq.QueryHandlers;

namespace Marten.Services
{
    public interface IStorageOperation: IQueryHandler
    {
        Type DocumentType { get; }
    }
}
