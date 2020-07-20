using System;
using System.Linq;
using Marten.Internal.Operations;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing.CoreFunctionality
{
    public static class UnitOfWorkExtensions
    {
        public static void ShouldHaveUpsertFor<T>(this IDocumentSession session, T document)
        {
            session.PendingChanges.Operations()
                .OfType<IDocumentStorageOperation>()

                .ShouldContain(x => x.Role() == OperationRole.Upsert && document.Equals(x.Document));
        }

        public static void ShouldHaveInsertFor<T>(this IDocumentSession session, T document)
        {
            session.PendingChanges.Operations()
                .OfType<IDocumentStorageOperation>()

                .ShouldContain(x => x.Role() == OperationRole.Insert && document.Equals(x.Document));
        }

        public static void ShouldHaveUpdateFor<T>(this IDocumentSession session, T document)
        {
            session.PendingChanges.Operations()
                .OfType<IDocumentStorageOperation>()

                .ShouldContain(x => x.Role() == OperationRole.Update && document.Equals(x.Document));
        }

        public static void ShouldHaveDeleteFor(this IDocumentSession session, User user)
        {
            session.PendingChanges.Operations()
                .OfType<DeleteOne<User, Guid>>()
                .ShouldContain(x => x.Id == user.Id);
        }

        public static void ShouldHaveDeleteFor(this IDocumentSession session, Target target)
        {
            session.PendingChanges.Operations()
                .OfType<DeleteOne<Target, Guid>>()
                .ShouldContain(x => x.Id == target.Id);
        }
    }
}
