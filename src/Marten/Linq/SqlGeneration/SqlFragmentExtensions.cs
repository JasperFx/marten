using System;
using System.Collections.Generic;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration
{
    [Obsolete("this needs to move to Weasel")]
    public static class SqlFragmentExtensions
    {
        public static void WriteFragments(this IList<ISqlFragment> fragments, CommandBuilder builder,
            string separator = " and ")
        {
            if (fragments.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(fragments), "Must be at least one ISqlFragment");

            fragments[0].Apply(builder);

            for (int i = 1; i < fragments.Count; i++)
            {
                builder.Append(separator);
                fragments[i].Apply(builder);
            }
        }
    }
}
