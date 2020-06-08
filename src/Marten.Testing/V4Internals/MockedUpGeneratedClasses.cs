using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Util;
using Marten.V4Internals;
using Npgsql;
using NpgsqlTypes;
using Remotion.Linq;
using IStorageOperation = Marten.V4Internals.IStorageOperation;

namespace Marten.Testing.V4Internals
{


    public class UserStorage: Marten.V4Internals.IDocumentStorage<User>
    {
        // This would be a setter
        public ISerializer Serializer { get; } = new JsonNetSerializer();

        public string[] SelectFields()
        {
            return new string[] {"", ""};
        }

        public void WriteSelectClause(CommandBuilder sql)
        {
            sql.Append("select field1, field2, field3 from table object ");
        }

        public DbObjectName TableName => new DbObjectName("foo");

        protected bool TryFindExisting(Guid id, IMartenSession session, out User user)
        {
            if (session.ItemMap.TryGetValue(typeof(User), out var dict))
            {
                if (dict is Dictionary<Guid, User> d)
                {
                    return (d.TryGetValue(id, out user));
                }
            }

            user = null;
            return false;
        }

        public User Resolve(DbDataReader reader, IMartenSession session)
        {
            var id = reader.GetFieldValue<Guid>(0);
            if (TryFindExisting(id, session, out var existing))
            {
                return existing;
            }


            User user;
            using (var json = reader.GetTextReader(1))
            {
                user = Serializer.FromJson<User>(json);
            }

            // If it's versioned...

            var version = reader.GetFieldValue<Guid>(2);

            // extra stuff for metadata

            StoreDocumentAndVersion(session, user, version);

            return user;
        }

        protected void StoreDocumentAndVersion(IMartenSession session, User user, Guid version)
        {
            throw new NotImplementedException();
        }

        public Task<User> ResolveAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            throw new NotImplementedException();
        }


        public void Load(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<User> documents,
            CharArrayTextWriter pool)
        {
            throw new NotImplementedException();
        }

        public string CreateTempTableForCopying()
        {
            throw new NotImplementedException();
        }

        public void LoadIntoTempTable(ITenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<User> documents,
            CharArrayTextWriter pool)
        {
            throw new NotImplementedException();
        }

        public string CopyNewDocumentsFromTempTable()
        {
            throw new NotImplementedException();
        }

        public string OverwriteDuplicatesFromTempTable()
        {
            throw new NotImplementedException();
        }

        public Guid? VersionFor(User document, IMartenSession session)
        {
            throw new NotImplementedException();
        }

        public void Store(IMartenSession session, User document)
        {
            throw new NotImplementedException();
        }

        public void Store(IMartenSession session, User document, Guid? version)
        {
            throw new NotImplementedException();
        }

        public void Eject(IMartenSession session, User document)
        {
            throw new NotImplementedException();
        }

        public IStorageOperation Update(User document, IMartenSession session, ITenant tenant)
        {
            throw new NotImplementedException();

            // Might poke in tenant too
            //return new UpdateUserOperation(document, session.Serializer);
        }

        public IStorageOperation Insert(User document, ITenant tenant)
        {
            throw new NotImplementedException();
        }

        public IStorageOperation Upsert(User document, IMartenSession session, ITenant tenant)
        {
            throw new NotImplementedException();
        }

        public IStorageOperation Override(User document, IMartenSession session, ITenant tenant)
        {
            throw new NotImplementedException();
        }

        public IStorageOperation DeleteForDocument(User document)
        {
            throw new NotImplementedException();
        }

        public IStorageOperation DeleteForWhere(IWhereFragment @where)
        {
            throw new NotImplementedException();
        }

        public IWhereFragment FilterDocuments(QueryModel model, IWhereFragment query)
        {
            throw new NotImplementedException();
        }

        public IWhereFragment DefaultWhereFragment()
        {
            throw new NotImplementedException();
        }

        public IFieldMapping Fields { get; }
        public Type IdType { get; }
    }

}
