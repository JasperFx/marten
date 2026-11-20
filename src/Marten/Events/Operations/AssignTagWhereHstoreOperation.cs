using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Dcb;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

using Marten.Services;

namespace Marten.Events.Operations;

/// <summary>
/// HStore-mode counterpart of <see cref="AssignTagWhereOperation"/>: retroactively
/// merges a single tag key-value into <c>mt_events.tags</c> for every row matching
/// a user-supplied WHERE clause. Uses the <c>hstore || hstore</c> concatenation
/// operator so existing tags on the row are preserved. Then moves the tag's
/// <c>mt_dcb_tag_version</c> rows (#6078, see <see cref="AssignedTagVersionMove"/>).
/// </summary>
internal class AssignTagWhereHstoreOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly EventGraph _events;
    private readonly string _schemaName;
    private readonly string _tagKey;
    private readonly string _versionValue;
    private readonly Dictionary<string, string> _tags;
    private readonly ISqlFragment _whereFragment;
    private readonly bool _isConjoined;

    /// <param name="versionValue">
    ///     The value in <see cref="JasperFx.Events.Tags.TagValueStringifier"/>'s form, as <c>mt_dcb_tag_version</c>
    ///     holds it.
    /// </param>
    public AssignTagWhereHstoreOperation(EventGraph events, string tagKey, string tagValue, string versionValue,
        ISqlFragment whereFragment, bool isConjoined)
    {
        _events = events;
        _schemaName = events.DatabaseSchemaName;
        _tagKey = tagKey;
        _versionValue = versionValue;
        _tags = new Dictionary<string, string>(1) { [tagKey] = tagValue };
        _whereFragment = whereFragment;
        _isConjoined = isConjoined;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append("update ");
        builder.Append(_schemaName);
        builder.Append(".mt_events as d set tags = coalesce(d.tags, ''::hstore) || ");
        var p = builder.AppendParameter(_tags);
        p.NpgsqlDbType = NpgsqlDbType.Hstore;
        builder.Append(" where ");
        _whereFragment.Apply(builder);
        if (_isConjoined)
        {
            builder.Append(" and d.tenant_id = ");
            builder.AppendParameter(session.TenantId);
        }

        // #6078: a statement of its own, after the tags are in
        builder.StartNewCommand();
        AssignedTagVersionMove.Append(builder, _events, _tagKey, _versionValue, session.TenantId, _whereFragment);
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}
