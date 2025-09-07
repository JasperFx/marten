using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration;
using Marten.Services;
using Marten.Util;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Patching;

internal class PatchFragment: IOperationFragment
{
    private const string VALUE_LOOKUP = "___VALUE___";
    private readonly ISerializer _serializer;
    private readonly IDocumentStorage _storage;
    private readonly DbObjectName _function;
    private readonly DocumentSessionBase _session;
    private readonly List<PatchData> _patchSet;

    public PatchFragment(DocumentSessionBase session, List<PatchData> patchSet, ISerializer serializer,
        DbObjectName function,
        IDocumentStorage storage)
    {
        _session = session;
        _patchSet = patchSet;
        _patchSet = patchSet;
        _serializer = serializer;
        _function = function;
        _storage = storage;
    }

    public void Apply(ICommandBuilder builder)
    {
        var patchSetStr = new List<string>();
        foreach (var patch in _patchSet)
        {
            var json = _serializer.ToCleanJson(patch.Items);
            if (patch.Items.TryGetValue("value", out var document))
            {
                var value = patch.PossiblyPolymorphic ? _serializer.ToJsonWithTypes(document) : _serializer.ToJson(document);
                var copy = new Dictionary<string, object>();
                foreach (var item in patch.Items) copy.Add(item.Key, item.Value);

                copy["value"] = VALUE_LOOKUP;

                var patchJson = _serializer.ToJson(copy);
                var replacedValue = patchJson.Replace($"\"{VALUE_LOOKUP}\"", value);

                json = replacedValue;
            }
            patchSetStr.Add(json);
        }

        builder.Append("update ");
        builder.Append(_storage.TableName.QualifiedName);
        builder.Append(" as d set data = ");
        builder.Append(_function.QualifiedName);
        builder.Append("(data, ");
        builder.AppendParameter("[" + string.Join(",", patchSetStr.ToArray()) + "]", NpgsqlDbType.Jsonb);
        builder.Append(")");

        if (_storage is IHaveMetadataColumns metadata)
        {
            foreach (var column in metadata.MetadataColumns().Where(x => x.Enabled && x.ShouldUpdatePartials))
            {
                builder.Append(", ");
                column.WriteMetadataInUpdateStatement(builder, _session);
            }
        }
    }

    public OperationRole Role()
    {
        return OperationRole.Patch;
    }
}

internal class PatchOperation: StatementOperation, NoDataReturnedCall
{
    private readonly IDocumentStorage _storage;
    private readonly List<PatchData> _patchSet;
    private readonly ISerializer _serializer;

    public PatchOperation(DocumentSessionBase session, DbObjectName function, IDocumentStorage storage,
        List<PatchData> patchSet, ISerializer serializer):
        base(storage, new PatchFragment(session, patchSet, serializer, function, storage))
    {
        _storage = storage;
        _patchSet = patchSet;
        _serializer = serializer;
    }

    public OperationRole Role()
    {
        return OperationRole.Patch;
    }

    protected override void configure(ICommandBuilder builder)
    {
        if (_patchSet.Count == 0) return;
        base.configure(builder);
        applyUpdates(builder);
    }

    private void applyUpdates(ICommandBuilder builder)
    {
        var fields = _storage.DuplicatedFields;
        if (!fields.Any())
        {
            return;
        }

        // Get the paths being modified by this patch operation
        var modifiedPaths = _patchSet
            .Select(patch => patch.Items["path"].ToString())
            .ToHashSet(System.StringComparer.Ordinal);

        // Only update duplicated fields where their mapping path is affected by the patch path
        var affectedFields = fields.Where(f => IsFieldAffectedByPatchPath(f, modifiedPaths)).ToList();

        if (affectedFields.Count == 0)
        {
            return;
        }

        builder.StartNewCommand();
        builder.Append("update ");
        builder.Append(_storage.TableName.QualifiedName);
        builder.Append(" as d set ");

        builder.Append(affectedFields[0].UpdateSqlFragment());
        for (var i = 1; i < affectedFields.Count; i++)
        {
            builder.Append(", ");
            builder.Append(affectedFields[i].UpdateSqlFragment());
        }

        writeWhereClause(builder);
    }

    private bool IsFieldAffectedByPatchPath(DuplicatedField field, HashSet<string> modifiedPaths)
    {
        // get the dot seperated path derived from field Members info
        var path = string.Join('.', field.Members.Select(x => x.Name.FormatCase(_serializer.Casing)));
        return modifiedPaths.Any(p => p.StartsWith(path, StringComparison.Ordinal));
    }
}
