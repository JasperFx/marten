using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples;

public class Deletes
{
    #region sample_delete_by_document_id

    internal Task DeleteByDocumentId(IDocumentSession session, Guid userId)
    {
        // Tell Marten the type and identity of a document to
        // delete
        session.Delete<User>(userId);

        return session.SaveChangesAsync();
    }

    #endregion

    #region sample_UndoDeletion

    internal Task UndoDeletion(IDocumentSession session, Guid userId)
    {
        // Tell Marten the type and identity of a document to
        // delete
        session.UndoDeleteWhere<User>(x => x.Id == userId);

        return session.SaveChangesAsync();
    }

    #endregion

    #region sample_AllDocumentTypesShouldBeSoftDeleted

    internal void AllDocumentTypesShouldBeSoftDeleted()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");
            opts.Policies.AllDocumentsSoftDeleted();
        });
    }

    #endregion

    #region sample_HardDeletes

    internal void ExplicitlyHardDelete(IDocumentSession session, User document)
    {
        // By document
        session.HardDelete(document);

        // By type and identity
        session.HardDelete<User>(document.Id);

        // By type and criteria
        session.HardDeleteWhere<User>(x => x.Roles.Contains("admin"));

        // And you still have to call SaveChanges()/SaveChangesAsync()
        // to actually perform the operations
    }

    #endregion


    #region sample_delete_by_document

    public Task DeleteByDocument(IDocumentSession session, User user)
    {
        session.Delete(user);
        return session.SaveChangesAsync();
    }

    #endregion

    #region sample_deletes
    public void delete_documents(IDocumentSession session)
    {
        var user = new User();

        session.Delete(user);
        session.SaveChanges();

        // OR

        session.Delete(user.Id);
        session.SaveChanges();
    }

    #endregion
}