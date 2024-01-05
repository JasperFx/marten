using Marten.Linq.Members;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Includes;

internal class IdInIncludedDocumentIdentifierFilter: ISqlFragment
{
    private readonly IQueryableMember _connectingMember;
    private readonly string _fromObject;

    public IdInIncludedDocumentIdentifierFilter(string fromObject, IQueryableMember connectingMember)
    {
        _fromObject = fromObject;
        _connectingMember = connectingMember;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("d.id in (select ");
        builder.Append(_connectingMember.LocatorForIncludedDocumentId);
        builder.Append(" from ");
        builder.Append(_fromObject);
        builder.Append(" as d)");
    }

}
