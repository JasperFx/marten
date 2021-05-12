using System;
using System.Linq;
using Marten.Internal.Operations;
using Marten.Linq.SqlGeneration;
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
                .OfType<Deletion>()
                .ShouldContain(x => x.Id.Equals(user.Id));
        }

        public static void ShouldHaveDeleteFor(this IDocumentSession session, Target target)
        {
            session.PendingChanges.Operations()
                .OfType<Deletion>()
                .ShouldContain(x => MatchesDeletion(target, x));
        }

        internal static bool MatchesDeletion(Target target, Deletion deletion)
        {
            if (deletion.Id.Equals(target.Id)) return true;

            if (deletion.Document is Target t) return t.Id == target.Id;

            return false;
        }
    }
}
