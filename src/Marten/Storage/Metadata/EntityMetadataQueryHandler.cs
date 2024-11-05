using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Weasel.Postgresql;
using StringExtensions = JasperFx.Core.StringExtensions;

namespace Marten.Storage.Metadata;

internal class EntityMetadataQueryHandler: IQueryHandler<DocumentMetadata>
{
    private readonly MetadataColumn[] _columns;
    private readonly object _id;
    private readonly IDocumentStorage _storage;

    public EntityMetadataQueryHandler(object id, IDocumentStorage storage)
    {
        _id = id;
        _storage = storage;

        SourceType = storage.DocumentType;

        if (storage is IHaveMetadataColumns m)
        {
            _columns = m.MetadataColumns();
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(storage));
        }
    }

    public Type SourceType { get; }

    public void ConfigureCommand(IPostgresqlCommandBuilder sql, IMartenSession session)
    {
        sql.Append("select id, ");

        var fields = StringExtensions.Join(_columns.Select(x => x.Name), ", ");

        sql.Append(fields);

        sql.Append(" from ");
        sql.Append(_storage.TableName.QualifiedName);
        sql.Append(" where id = ");
        sql.AppendParameter(_id);
    }

    public DocumentMetadata Handle(DbDataReader reader, IMartenSession session)
    {
        if (!reader.Read())
        {
            return null;
        }

        var id = reader.GetFieldValue<object>(0);
        var metadata = new DocumentMetadata(id);

        for (var i = 0; i < _columns.Length; i++)
        {
            _columns[i].Apply(session, metadata, i + 1, reader);
        }

        return metadata;
    }

    public async Task<DocumentMetadata> HandleAsync(DbDataReader reader, IMartenSession session,
        CancellationToken token)
    {
        var hasAny = await reader.ReadAsync(token).ConfigureAwait(false);
        if (!hasAny)
        {
            return null;
        }

        var id = await reader.GetFieldValueAsync<object>(0, token).ConfigureAwait(false);
        var metadata = new DocumentMetadata(id);

        for (var i = 0; i < _columns.Length; i++)
        {
            await _columns[i].ApplyAsync(session, metadata, i + 1, reader, token).ConfigureAwait(false);
        }

        return metadata;
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotSupportedException();
    }
}
