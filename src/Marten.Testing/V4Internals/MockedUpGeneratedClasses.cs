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
    public abstract class DocOperation : IStorageOperation
    {
        private readonly User _doc;
        public const string Command = "some command";

        public DocOperation(User doc)
        {
            _doc = doc;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameters = builder.AppendWithParameters(Command);

            // Id
            parameters[0].NpgsqlDbType = NpgsqlDbType.Uuid;
            parameters[0].Value = _doc.Id;

            // Document
            parameters[1].NpgsqlDbType = NpgsqlDbType.Jsonb;
            parameters[1].Value = session.Serializer.ToJson(_doc);

            // What else?
        }

        public Type DocumentType => typeof(User);
        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            // Nothing for now
        }

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            // Nothing for now
            return Task.CompletedTask;
        }

        public StorageRole Role => StorageRole.Deletion;
    }

    public class UpsertUserOperation: IStorageOperation
    {
        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            throw new NotImplementedException();
        }

        public Type DocumentType { get; } = typeof(User);
        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            throw new NotImplementedException();
        }

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public StorageRole Role { get; }
    }

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

    public class UpdateUserOperation: IStorageOperation
    {
        private readonly User _user;

        // Might also be the tenant and version
        public UpdateUserOperation(User user)
        {
            _user = user;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            //var parameters = builder.AppendWithParameters("select mt_update_user(?, ?, ?)");
            var parameters = new NpgsqlParameter[5];

            parameters[0].NpgsqlDbType = NpgsqlDbType.Integer;
            parameters[0].Value = _user.Id;

            // and more!
        }

        public Type DocumentType => typeof(User);

        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            // Nothing
        }

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        public StorageRole Role => StorageRole.Update;
    }
}
