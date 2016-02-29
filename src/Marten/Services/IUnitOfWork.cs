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

        // TODO -- needs tests
        IEnumerable<object> Inserts();
        IEnumerable<T> UpdatesFor<T>();

        // TODO -- needs test
        IEnumerable<T> InsertsFor<T>();

        // TODO -- needs test
        IEnumerable<T> AllChangedFor<T>();
    }
}