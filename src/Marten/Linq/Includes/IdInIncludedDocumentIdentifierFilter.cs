#nullable enable
using Marten.Linq.Members;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Includes;

internal class IdInIncludedDocumentIdentifierFilter: ISqlFragment
{
    private readonly IQueryableMember _connectingMember;
    private readonly IQueryableMember? _identifyingMember;
    private readonly string _fromObject;

    public IdInIncludedDocumentIdentifierFilter(string fromObject, IQueryableMember connectingMember, IQueryableMember? identifyingMember)
    {
        _fromObject = fromObject;
        _connectingMember = connectingMember;
        _identifyingMember = identifyingMember;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_identifyingMember is null ? "d.id" : _identifyingMember.LocatorForIncludedDocumentId);
        builder.Append(" in (select ");
        builder.Append(_connectingMember.LocatorForIncludedDocumentId);
        builder.Append(" from ");
        builder.Append(_fromObject);
        builder.Append(" as d)");
    }

}
