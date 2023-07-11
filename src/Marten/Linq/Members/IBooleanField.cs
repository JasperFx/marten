using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal interface IBooleanField
{
    ISqlFragment BuildIsTrueFragment();
}
