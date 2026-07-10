#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using ImTools;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Linq.Parsing.Operators;
using Marten.Linq.SoftDeletes;
using Marten.Schema;
using Marten.Storage;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class DocumentQueryableMemberCollection: IQueryableMemberCollection, IQueryableMember
{
    private readonly StoreOptions _options;
    private readonly DocumentQueryableMemberCollection? _inheritedMembers;
    private ImHashMap<string, IQueryableMember> _members = ImHashMap<string, IQueryableMember>.Empty;

    public DocumentQueryableMemberCollection(IDocumentMapping mapping, StoreOptions options)
        : this(mapping, options, null)
    {
    }

    /// <summary>
    /// For a subclass mapping. A subclass shares its parent's physical table, so the parent's
    /// column-backed members — duplicated fields, the id column, the soft-delete flag — resolve to real
    /// columns (<c>d.&lt;column&gt;</c>) that are valid verbatim in a subclass query. Inheriting them lets a
    /// <c>Query&lt;Subclass&gt;()</c> filter hit the index instead of falling through to a JSONB member
    /// (<c>CAST(d.data -&gt;&gt; '...')</c>). See #4916. The inheritance is consulted lazily at query time
    /// because the parent registers these members during store compilation, after this collection is built.
    /// </summary>
    public DocumentQueryableMemberCollection(IDocumentMapping mapping, StoreOptions options,
        DocumentQueryableMemberCollection? inheritedMembers)
    {
        _options = options;
        ElementType = mapping.DocumentType;
        _inheritedMembers = inheritedMembers;
    }

    public TenancyStyle TenancyStyle { get; set; } = TenancyStyle.Single;

    public string MemberName => string.Empty;

    public string JsonPathSegment => "";

    void ISqlFragment.Apply(ICommandBuilder builder)
    {
        builder.Append("d.data");
    }

    Type IQueryableMember.MemberType => ElementType;

    string IQueryableMember.TypedLocator => "d.data";

    string IQueryableMember.RawLocator => "d.data";

    string IQueryableMember.JSONBLocator => "d.data";

    string IQueryableMember.BuildOrderingExpression(Ordering ordering, CasingRule casingRule)
    {
        throw new NotSupportedException();
    }

    IQueryableMember[] IQueryableMember.Ancestors => Array.Empty<IQueryableMember>();

    Dictionary<string, object> IQueryableMember.FindOrPlaceChildDictionaryForContainment(
        Dictionary<string, object> dict)
    {
        throw new NotSupportedException();
    }

    void IQueryableMember.PlaceValueInDictionaryForContainment(Dictionary<string, object> dict,
        ConstantExpression constant)
    {
        throw new NotSupportedException();
    }

    string IQueryableMember.LocatorForIncludedDocumentId => throw new NotSupportedException();
    public string NullTestLocator { get; } = "d.data";

    public string SelectorForDuplication(string pgType)
    {
        throw new NotSupportedException();
    }

    public Type ElementType { get; }

    public IQueryableMember FindMember(MemberInfo member)
    {
        if (_members.TryFind(member.Name, out var m))
        {
            return m;
        }

        // #4916: a subclass query resolves its members against this (initially empty) collection, so a
        // duplicated field or the base id — registered only on the parent mapping — would otherwise fall
        // through to a JSONB member. Reuse the parent's column-backed member instead; it locates a real
        // column on the table both share.
        if (_inheritedMembers != null && _inheritedMembers.TryFindColumnBackedMember(member.Name, out var inherited))
        {
            _members = _members.AddOrUpdate(member.Name, inherited);
            return inherited;
        }

        m = _options.CreateQueryableMember(member, this);
        _members = _members.AddOrUpdate(member.Name, m);

        return m;
    }

    /// <summary>
    /// Whether this collection already holds a member for <paramref name="name"/> that is backed by a real
    /// column on the document table (a duplicated field, the id, or the soft-delete flag) rather than a JSONB
    /// locator. A subclass collection reuses exactly these from its parent; see the inheriting constructor.
    /// Only members registered up front (never lazily-created JSONB members) are considered, so the parent's
    /// own query history cannot leak a <c>d.data -&gt;&gt; ...</c> member into a subclass query.
    /// </summary>
    internal bool TryFindColumnBackedMember(string name, out IQueryableMember member)
    {
        if (_members.TryFind(name, out member!) && member is DuplicatedField or IdMember or IsSoftDeletedMember)
        {
            return true;
        }

        member = null!;
        return false;
    }

    public void ReplaceMember(MemberInfo member, IQueryableMember queryableMember)
    {
        _members = _members.AddOrUpdate(member.Name, queryableMember);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IEnumerator<IQueryableMember> GetEnumerator()
    {
        return _members.Enumerate().Select(x => x.Value).GetEnumerator();
    }

    public void RemoveAnyIdentityMember()
    {
        var newMembers = ImHashMap<string, IQueryableMember>.Empty;
        foreach (var pair in _members.Enumerate())
        {
            if (pair.Value is IdMember)
            {
                continue;
            }

            newMembers = newMembers.AddOrUpdate(pair.Key, pair.Value);
        }

        _members = newMembers;
    }
}
