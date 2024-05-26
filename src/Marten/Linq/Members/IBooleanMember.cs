#nullable enable
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal interface IBooleanMember
{
    ISqlFragment BuildIsTrueFragment();
}
