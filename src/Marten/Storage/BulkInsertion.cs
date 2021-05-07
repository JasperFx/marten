using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Weasel.Postgresql;
using Marten.Schema.BulkLoading;
using Marten.Util;
using Npgsql;

namespace Marten.Storage
{
    internal class BulkInsertion: IDisposable
    {
        private readonly ITenant _tenant;

        public BulkInsertion(ITenant tenant, StoreOptions options)
        {
            _tenant = tenant;
            Serializer = options.Serializer();
        }

        public ISerializer Serializer { get; }

        public void Dispose()
        {
        }

        public void BulkInsert<T>(IReadOnlyCollection<T> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000)
        {
            if (typeof(T) == typeof(object))
            {
                BulkInsertDocuments(documents.OfType<object>(), mode);
            }
            else
            {
                using var conn = _tenant.CreateConnection();
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

        public async Task BulkInsertAsync<T>(IReadOnlyCollection<T> documents, BulkInsertMode mode, int batchSize, CancellationToken cancellation)
        {
            if (typeof(T) == typeof(object))
            {
                await BulkInsertDocumentsAsync(documents.OfType<object>(), mode, batchSize, cancellation);
            }
            else
            {
                await using var conn = _tenant.CreateConnection();
                await conn.OpenAsync(cancellation);

#if NETSTANDARD2_0
                var tx = conn.BeginTransaction();

                #else
                var tx = await conn.BeginTransactionAsync(cancellation);

#endif

                try
                {
                    await bulkInsertDocumentsAsync(documents, batchSize, conn, mode, cancellation);

                    await tx.CommitAsync(cancellation);
                }
                catch (Exception)
                {
                    await tx.RollbackAsync(cancellation);
                    throw;
                }
            }
        }



        public void BulkInsertDocuments(IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000)
        {
            var groups =
                documents.Where(x => x != null)
                    .GroupBy(x => x.GetType())
                    .Select(group => typeof(BulkInserter<>).CloseAndBuildAs<IBulkInserter>(group, group.Key))
                    .ToArray();

            using var conn = _tenant.CreateConnection();

            conn.Open();
            var tx = conn.BeginTransaction();

            try
            {
                foreach (var group in groups) @group.BulkInsert(batchSize, conn, this, mode);

                tx.Commit();
            }
            catch (Exception)
            {
                tx.Rollback();
                throw;
            }
        }

        public async Task BulkInsertDocumentsAsync(IEnumerable<object> documents, BulkInsertMode mode, int batchSize, CancellationToken cancellation)
        {
            var groups =
                documents.Where(x => x != null)
                    .GroupBy(x => x.GetType())
                    .Select(group => typeof(BulkInserter<>).CloseAndBuildAs<IBulkInserter>(group, group.Key))
                    .ToArray();

            using var conn = _tenant.CreateConnection();

            await conn.OpenAsync(cancellation);
#if NETSTANDARD2_0
            var tx = conn.BeginTransaction();
            #else
            var tx = await conn.BeginTransactionAsync(cancellation);
#endif

            try
            {
                foreach (var group in groups)
                {
                    await @group.BulkInsertAsync(batchSize, conn, this, mode, cancellation);
                }

                await tx.CommitAsync(cancellation);
            }
            catch (Exception)
            {
                await tx.RollbackAsync(cancellation);
                throw;
            }
        }

        private void bulkInsertDocuments<T>(IReadOnlyCollection<T> documents, int batchSize, NpgsqlConnection conn,
            BulkInsertMode mode)
        {
            var provider = _tenant.Providers.StorageFor<T>();
            var loader = provider.BulkLoader;

            if (mode != BulkInsertMode.InsertsOnly)
            {
                var sql = loader.CreateTempTableForCopying();
                conn.CreateCommand(sql).ExecuteNonQuery();
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

                    if (batch.Count < batchSize) continue;

                    loadDocuments(batch, loader, mode, conn);
                    batch.Clear();
                }

                loadDocuments(batch, loader, mode, conn);
            }

            if (mode == BulkInsertMode.IgnoreDuplicates)
            {
                var copy = loader.CopyNewDocumentsFromTempTable();

                conn.CreateCommand(copy).ExecuteNonQuery();
            }
            else if (mode == BulkInsertMode.OverwriteExisting)
            {
                var overwrite = loader.OverwriteDuplicatesFromTempTable();
                var copy = loader.CopyNewDocumentsFromTempTable();

                conn.CreateCommand(overwrite + ";" + copy).ExecuteNonQuery();
            }
        }

        private async Task bulkInsertDocumentsAsync<T>(IReadOnlyCollection<T> documents, int batchSize, NpgsqlConnection conn, BulkInsertMode mode, CancellationToken cancellation)
        {
            var provider = _tenant.Providers.StorageFor<T>();
            var loader = provider.BulkLoader;

            if (mode != BulkInsertMode.InsertsOnly)
            {
                var sql = loader.CreateTempTableForCopying();
                await conn.CreateCommand(sql).ExecuteNonQueryAsync(cancellation);
            }

            if (documents.Count <= batchSize)
            {
                await loadDocumentsAsync(documents, loader, mode, conn, cancellation);
            }
            else
            {
                var batch = new List<T>(batchSize);

                foreach (var document in documents)
                {
                    batch.Add(document);

                    if (batch.Count < batchSize) continue;

                    await loadDocumentsAsync(batch, loader, mode, conn, cancellation);
                    batch.Clear();
                }

                await loadDocumentsAsync(batch, loader, mode, conn, cancellation);
            }

            if (mode == BulkInsertMode.IgnoreDuplicates)
            {
                var copy = loader.CopyNewDocumentsFromTempTable();

                await conn.CreateCommand(copy).ExecuteNonQueryAsync(cancellation);
            }
            else if (mode == BulkInsertMode.OverwriteExisting)
            {
                var overwrite = loader.OverwriteDuplicatesFromTempTable();
                var copy = loader.CopyNewDocumentsFromTempTable();

                await conn.CreateCommand(overwrite + ";" + copy)
                    .ExecuteNonQueryAsync(cancellation);
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

        private async Task loadDocumentsAsync<T>(IReadOnlyCollection<T> documents, IBulkLoader<T> loader, BulkInsertMode mode, NpgsqlConnection conn, CancellationToken cancellation)
        {
            if (mode == BulkInsertMode.InsertsOnly)
            {
                await loader.LoadAsync(_tenant, Serializer, conn, documents, cancellation);
            }
            else
            {
                await loader.LoadIntoTempTableAsync(_tenant, Serializer, conn, documents, cancellation);
            }
        }

        internal interface IBulkInserter
        {
            void BulkInsert(int batchSize, NpgsqlConnection connection, BulkInsertion parent, BulkInsertMode mode);
            Task BulkInsertAsync(int batchSize, NpgsqlConnection conn, BulkInsertion bulkInsertion, BulkInsertMode mode, CancellationToken cancellation);
        }

        internal class BulkInserter<T>: IBulkInserter
        {
            private readonly T[] _documents;

            public BulkInserter(IEnumerable<object> documents)
            {
                _documents = documents.OfType<T>().ToArray();
            }

            public void BulkInsert(int batchSize, NpgsqlConnection connection, BulkInsertion parent,
                BulkInsertMode mode)
            {
                parent.bulkInsertDocuments(_documents, batchSize, connection, mode);
            }

            public Task BulkInsertAsync(int batchSize, NpgsqlConnection conn, BulkInsertion parent, BulkInsertMode mode,
                CancellationToken cancellation)
            {
                return parent.bulkInsertDocumentsAsync(_documents, batchSize, conn, mode, cancellation);
            }
        }




    }
}
