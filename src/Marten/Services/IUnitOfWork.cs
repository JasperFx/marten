using System;
using System.Collections.Generic;

namespace Marten.Services
{
    public interface IUnitOfWork
    {
        IEnumerable<PendingDeletion> Deletions();
        IEnumerable<PendingDeletion> DeletionsFor<T>();
        IEnumerable<PendingDeletion> DeletionsFor(Type documentType);
        IEnumerable<object> Updates();
        IEnumerable<T> UpdatesFor<T>();
    }
}