using System;
using System.Collections.Generic;

namespace Marten.Linq.Members;

public interface IQueryableMemberCollection: IHasChildrenMembers, IEnumerable<IQueryableMember>
{
    Type ElementType { get; }
}
