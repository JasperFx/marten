using System;

namespace Marten.Services
{
    public interface IDeletion : IStorageOperation, NoDataReturnedCall
    {
        Type DocumentType { get; }
    }
}