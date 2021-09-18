using System;
using System.Collections.Generic;
using System.Linq;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration
{
    [Obsolete("this needs to move to Weasel")]
    public static class SqlFragmentExtensions
    {

        public static ISqlFragment CombineFragments(this IList<ISqlFragment> fragments)
        {
            switch (fragments.Count)
            {
                case 0:
                    return null;

                case 1:
                    return fragments.Single();

                default:
                    return CompoundWhereFragment.And(fragments);
            }
        }

    }


}
