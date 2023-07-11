using System;
using System.Reflection;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;

namespace Marten.Linq.QueryHandlers;

internal class LinqConstants
{
    internal static readonly string StatsColumn = "count(1) OVER() as total_rows";
    internal static readonly string IdListTableName = "mt_temp_id_list";

    internal static readonly ISelector<string> StringValueSelector =
        new ScalarStringSelectClause(string.Empty, string.Empty);

    internal static readonly string CONTAINS = nameof(string.Contains);
    internal static readonly string ANY = "Any";
    internal static readonly string ALL = "All";
    internal static readonly PropertyInfo ArrayLength = typeof(Array).GetProperty(nameof(Array.Length));
}
