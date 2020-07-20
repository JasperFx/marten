using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Schema.BulkLoading;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Storage
{
    public class BulkInsertion: IDisposable
    {
        private readonly ITenant _tenant;

        public BulkInsertion(ITenant tenant, StoreOptions options)
        {
            _tenant = tenant;
            Serializer = options.Serializer();
        }

        public ISerializer Serializer { get; }

        public void BulkInsert<T>(IReadOnlyCollection<T> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000)
        {
            if (typeof(T) == typeof(object))
            {
                BulkInsertDocuments(documents.OfType<object>(), mode: mode);
            }
            else
            {
                using (var conn = _tenant.CreateConnection())
                {
                    conn.Open();
                    var tx = conn.BeginTransaction();

                    try
                    {
                        bulkInsertDocuments(documents, batchSize, conn, mode);

                        tx.Commit();
                    }
                    catch (Exception)
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        public void BulkInsertDocuments(IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000)
        {
            var groups =
                documents.Where(x => x != null)
                    .GroupBy(x => x.GetType())
                    .Select(group => typeof(BulkInserter<>).CloseAndBuildAs<IBulkInserter>(@group, @group.Key))
                    .ToArray();

            using (var conn = _tenant.CreateConnection())
            {
                conn.Open();
                var tx = conn.BeginTransaction();

                try
                {
                    foreach (var @group in groups)
                    {
                        @group.BulkInsert(batchSize, conn, this, mode);
                    }

                    tx.Commit();
                }
                catch (Exception)
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        internal interface IBulkInserter
        {
            void BulkInsert(int batchSize, NpgsqlConnection connection, BulkInsertion parent, BulkInsertMode mode);
        }

        internal class BulkInserter<T>: IBulkInserter
        {
            private readonly T[] _documents;

            public BulkInserter(IEnumerable<object> documents)
            {
                _documents = documents.OfType<T>().ToArray();
            }

            public void BulkInsert(int batchSize, NpgsqlConnection connection, BulkInsertion parent, BulkInsertMode mode)
            {
                parent.bulkInsertDocuments(_documents, batchSize, connection, mode);
            }
        }

        private void bulkInsertDocuments<T>(IReadOnlyCollection<T> documents, int batchSize, NpgsqlConnection conn, BulkInsertMode mode)
        {
            var provider = _tenant.Providers.StorageFor<T>();
            var loader = provider.BulkLoader;

            if (mode != BulkInsertMode.InsertsOnly)
            {
                var sql = loader.CreateTempTableForCopying();
                conn.RunSql(sql);
            }

            if (documents.Count <= batchSize)
            {
                loadDocuments(documents, loader, mode, conn);
            }
            else
            {
                var batch = new List<T>(batchSize);

                foreach (var document in documents)
                {
                    batch.Add(document);

                    if (batch.Count < batchSize)
                    {
                        continue;
                    }

                    loadDocuments(batch, loader, mode, conn);
                    batch.Clear();
                }

                loadDocuments(batch, loader, mode, conn);
            }

            if (mode == BulkInsertMode.IgnoreDuplicates)
            {
                var copy = loader.CopyNewDocumentsFromTempTable();

                conn.RunSql(copy);
            }
            else if (mode == BulkInsertMode.OverwriteExisting)
            {
                var overwrite = loader.OverwriteDuplicatesFromTempTable();
                var copy = loader.CopyNewDocumentsFromTempTable();

                conn.RunSql(overwrite, copy);
            }
        }

        private void loadDocuments<T>(IEnumerable<T> documents, IBulkLoader<T> loader, BulkInsertMode mode,
            NpgsqlConnection conn)
        {
            if (mode == BulkInsertMode.InsertsOnly)
            {
                loader.Load(_tenant, Serializer, conn, documents);
            }
            else
            {
                loader.LoadIntoTempTable(_tenant, Serializer, conn, documents);
            }
        }

        public void Dispose()
        {
        }
    }
}
