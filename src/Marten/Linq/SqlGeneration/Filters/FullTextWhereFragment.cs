#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Marten.Linq.Parsing.Methods.FullText;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Schema.Indexing.FullText;
using Marten.Util;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using Weasel.Postgresql.Tables.Indexes;

namespace Marten.Linq.SqlGeneration.Filters;

internal class FullTextWhereFragment: ISqlFragment
{
    private readonly string _vector;
    private readonly string _regConfig;
    private readonly FullTextSearchFunction _searchFunction;
    private readonly string _searchTerm;

    public FullTextWhereFragment(DocumentMapping? mapping, FullTextSearchFunction searchFunction, string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig)
    {
        // GHSA-vmw2-qwm8-x84c / GHSA-frqq-p5g3-8jq5: shared with every other regConfig sink.
        RegConfigValidation.Validate(regConfig);

        _regConfig = regConfig;

        _vector = FullTextIndexResolver.ResolveVector(mapping, regConfig);
        _searchFunction = searchFunction;
        _searchTerm = searchTerm;
    }

    // don't parameterize full-text search config as it ruins the performance with the query plan in PG
    private string Sql => $"{_vector} @@ {_searchFunction}('{_regConfig}'::regconfig, ?)";

    public void Apply(ICommandBuilder builder)
    {
        builder.AppendWithParameters(Sql)[0].Value = _searchTerm;
    }

}
