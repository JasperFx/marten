using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Schema.Indexing.Unique;
using Marten.Storage.Metadata;
using Marten.Util;
using Weasel.Postgresql.Tables;

namespace Marten.Schema;

public class ComputedIndex: IndexDefinition
{
    public enum Casings
    {
        /// <summary>
        ///     Leave the casing as is (default)
        /// </summary>
        Default,

        /// <summary>
        ///     Change the casing to uppercase
        /// </summary>
        Upper,

        /// <summary>
        ///     Change the casing to lowercase
        /// </summary>
        Lower
    }

    private readonly DocumentMapping _mapping;
    private readonly MemberInfo[][] _members;

    public ComputedIndex(DocumentMapping mapping, MemberInfo[] memberPath)
        : this(mapping, new[] { memberPath })
    {
    }

    public ComputedIndex(DocumentMapping mapping, MemberInfo[][] members)
    {
        _members = members;
        _mapping = mapping;
    }

    /// <summary>
    ///     Marks the column value as upper/lower casing
    /// </summary>
    public Casings Casing { get; set; }

    /// <summary>
    ///     Specifies the unique index is scoped to the tenant
    /// </summary>
    public TenancyScope TenancyScope { get; set; }

    public override string[] Columns
    {
        get => buildColumns().ToArray();
        set
        {
            // nothing
        }
    }

    protected override string deriveIndexName()
    {
        var name = _mapping.TableName.Name;

        name += IsUnique ? "_uidx_" : "_idx_";

        foreach (var member in _members) name += member.ToTableAlias();

        return name;
    }

    private IEnumerable<string> buildColumns()
    {
        foreach (var m in _members)
        {
            var field = _mapping.FieldFor(m);
            var casing = Casing;
            if (field.FieldType != typeof(string))
            {
                // doesn't make sense to lower-case this particular member
                casing = Casings.Default;
            }

            var sql = field.FieldType.IsEnumerable()
                ? field.RawLocator.Replace("d.", "")
                : field.TypedLocator.Replace("d.", "");
            switch (casing)
            {
                case Casings.Upper:
                    yield return $"upper({sql})";
                    break;

                case Casings.Lower:
                    yield return $"lower({sql})";
                    break;

                default:
                    yield return $"({sql})";
                    break;
            }
        }

        if (TenancyScope == TenancyScope.PerTenant)
        {
            yield return TenantIdColumn.Name;
        }
    }
}
