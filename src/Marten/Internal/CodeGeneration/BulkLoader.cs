using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal.Storage;
using Marten.Schema.BulkLoading;
using Marten.Storage;
using Npgsql;

namespace Marten.Internal.CodeGeneration;

public abstract class BulkLoader<T, TId>: IBulkLoader<T> where TId : notnull where T : notnull
{
    private readonly IDocumentStorage<T, TId> _storage;

    public BulkLoader(IDocumentStorage<T, TId> storage)
    {
        _storage = storage;
    }

    public async Task LoadAsync(Tenant tenant, ISerializer serializer, NpgsqlConnection conn,
        IEnumerable<T> documents,
        CancellationToken cancellation)
    {
        await using var writer = await conn.BeginBinaryImportAsync(MainLoaderSql(), cancellation).ConfigureAwait(false);

        foreach (var document in documents)
        {
            _storage.AssignIdentity(document, tenant.TenantId, tenant.Database);
            await writer.StartRowAsync(cancellation).ConfigureAwait(false);
            await LoadRowAsync(writer, document, tenant, serializer, cancellation).ConfigureAwait(false);
        }

        await writer.CompleteAsync(cancellation).ConfigureAwait(false);
    }


    public abstract string CreateTempTableForCopying();

    public async Task LoadIntoTempTableAsync(Tenant tenant, ISerializer serializer, NpgsqlConnection conn,
        IEnumerable<T> documents,
        CancellationToken cancellation)
    {
        await using var writer = await conn.BeginBinaryImportAsync(TempLoaderSql(), cancellation).ConfigureAwait(false);
        foreach (var document in documents)
        {
            await writer.StartRowAsync(cancellation).ConfigureAwait(false);
            await LoadRowAsync(writer, document, tenant, serializer, cancellation).ConfigureAwait(false);
        }

        await writer.CompleteAsync(cancellation).ConfigureAwait(false);
    }

    public abstract string CopyNewDocumentsFromTempTable();

    public abstract string OverwriteDuplicatesFromTempTable();

    public object GetNullable<TValue>(TValue? value) where TValue : struct
    {
        return value.HasValue ? value.Value : DBNull.Value;
    }

    public int GetEnumIntValue<TEnum>(TEnum? value) where TEnum : struct
    {
        if (value.HasValue)
        {
            return value.Value.As<int>();
        }

        return 0;
    }

    public string GetEnumStringValue<TEnum>(TEnum? value) where TEnum : struct
    {
        if (value.HasValue)
        {
            return value.Value.ToString()!;
        }

        return "EMPTY";
    }

    public abstract Task LoadRowAsync(NpgsqlBinaryImporter writer, T document, Tenant tenant,
        ISerializer serializer, CancellationToken cancellation);


    public abstract string MainLoaderSql();
    public abstract string TempLoaderSql();
}
