using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Schema.Arguments;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Storage.Metadata;

internal class UserNameColumn: MetadataColumn<string>, ISelectableColumn, IEventTableColumn
{
    public static readonly string ColumnName = "user_name";

    private static readonly Lazy<Action<DbDataReader, int, IEvent>> ReadSync =
        new(() => EventColumnReaders.BuildSync(x => x.UserName));

    private static readonly Lazy<Func<DbDataReader, int, IEvent, CancellationToken, Task>> ReadAsyncDelegate =
        new(() => EventColumnReaders.BuildAsync(x => x.UserName));

    public UserNameColumn(): base(ColumnName, x => x.LastModifiedBy)
    {
        Enabled = false;
        ShouldUpdatePartials = true;
    }

    public bool ShouldSelect(DocumentMapping mapping, StorageStyle storageStyle)
    {
        return mapping.Metadata.LastModifiedBy.EnabledWithMember();
    }

    internal override UpsertArgument ToArgument()
    {
        return new UserNameArgument();
    }

    public override void WriteMetadataInUpdateStatement(ICommandBuilder builder, DocumentSessionBase session)
    {
        builder.Append(ColumnName);
        builder.Append(" = ");
        builder.AppendParameter(session.CurrentUserName);
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }

    void IEventTableColumn.ReadValueSync(DbDataReader reader, int index, IEvent @event)
    {
        if (reader.IsDBNull(index)) return;
        ReadSync.Value(reader, index, @event);
    }

    async Task IEventTableColumn.ReadValueAsync(DbDataReader reader, int index, IEvent @event, CancellationToken cancellation)
    {
        if (await reader.IsDBNullAsync(index, cancellation).ConfigureAwait(false)) return;
        await ReadAsyncDelegate.Value(reader, index, @event, cancellation).ConfigureAwait(false);
    }
}

internal class UserNameArgument: UpsertArgument
{
    public UserNameArgument()
    {
        Arg = "userName";
        Column = UserNameColumn.ColumnName;
        PostgresType = "varchar";
        DbType = NpgsqlDbType.Varchar;
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}
