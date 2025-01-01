using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration;
using Marten.PLv8.Transforms;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Util;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.PLv8.Patching;

internal class PatchFragment: IOperationFragment
{
    private const string VALUE_LOOKUP = "___VALUE___";
    private readonly IDictionary<string, object> _patch;
    private readonly ISerializer _serializer;
    private readonly IDocumentStorage _storage;
    private readonly TransformFunction _transform;

    public PatchFragment(IDictionary<string, object> patch, ISerializer serializer, TransformFunction transform,
        IDocumentStorage storage, bool possiblyPolymorphic)
    {
        PossiblyPolymorphic = possiblyPolymorphic;
        _patch = patch;
        _serializer = serializer;
        _transform = transform;
        _storage = storage;
    }

    public bool PossiblyPolymorphic { get; }

    public void Apply(ICommandBuilder builder)
    {
        var json = _serializer.ToCleanJson(_patch);
        if (_patch.TryGetValue("value", out var document))
        {
            var value = PossiblyPolymorphic ? _serializer.ToJsonWithTypes(document) : _serializer.ToJson(document);
            var copy = new Dictionary<string, object>();
            foreach (var item in _patch) copy.Add(item.Key, item.Value);

            copy["value"] = VALUE_LOOKUP;

            var patchJson = _serializer.ToJson(copy);
            var replacedValue = patchJson.Replace($"\"{VALUE_LOOKUP}\"", value);

            json = replacedValue;
        }

        builder.Append("update ");
        builder.Append(_storage.TableName.QualifiedName);
        builder.Append(" as d set data = ");
        builder.Append(_transform.Identifier.QualifiedName);
        builder.Append("(data, ");
        builder.AppendParameter(json, NpgsqlDbType.Jsonb);
        builder.Append("), ");
        builder.Append(SchemaConstants.LastModifiedColumn);
        builder.Append(" = (now() at time zone 'utc'), ");
        builder.Append(SchemaConstants.VersionColumn);
        builder.Append(" = ");
        builder.AppendParameter(CombGuidIdGeneration.NewGuid());
    }

    public OperationRole Role()
    {
        return OperationRole.Patch;
    }
}

internal class PatchOperation: StatementOperation, NoDataReturnedCall
{
    private readonly ISqlFragment _fragment;
    private readonly IDocumentStorage _storage;
    private readonly IDictionary<string, object> _patch;
    private readonly ISerializer _serializer;

    public PatchOperation(TransformFunction transform, IDocumentStorage storage,
        IDictionary<string, object> patch, ISerializer serializer, bool possiblyPolymorphic): base(storage,
        new PatchFragment(patch, serializer, transform, storage, possiblyPolymorphic))
    {
        _patch = patch;
        _storage = storage;
        _serializer = serializer;
    }

    public OperationRole Role()
    {
        return OperationRole.Patch;
    }

    protected override void configure(ICommandBuilder builder)
    {
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

        // Only update duplicated fields where their mapping path is affected by the patch path
        var affectedFields = fields.Where(f => IsFieldAffectedByPatchPath(f, _patch["path"].ToString())).ToList();

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

    private bool IsFieldAffectedByPatchPath(DuplicatedField field, string modifiedPath)
    {
        // get the dot seperated path derived from field Members info
        var path = string.Join('.', field.Members.Select(x => x.Name.FormatCase(_serializer.Casing)));
        return modifiedPath.StartsWith(path, StringComparison.Ordinal);
    }
}
