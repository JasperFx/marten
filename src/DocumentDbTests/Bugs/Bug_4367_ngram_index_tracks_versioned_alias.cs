using System;
using System.Linq;
using Marten;
using Marten.Schema;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

/// <summary>
/// Regression for #4367. <see cref="NgramIndex"/> used to capture the
/// <c>DocumentMapping.TableName</c> snapshot in its constructor, so any
/// post-construction alias mutation (most importantly the <c>_{Version}</c>
/// suffix appended by <c>ProjectionVersionAliasPolicy</c> when a document is
/// owned by a versioned aggregate projection) wasn't reflected in the derived
/// index name. The index would end up named after the original alias while the
/// table itself carried the versioned alias — bumping the projection version
/// would then fail with <c>Npgsql.PostgresException 42P07: relation
/// "mt_doc_..._idx_ngram_..." already exists</c> on the second deployment
/// because the old (un-versioned) index still occupied the namespace.
///
/// The fix has <see cref="NgramIndex"/> hold a reference to the parent
/// <see cref="DocumentMapping"/> and resolve <c>TableName.Name</c> lazily,
/// matching <see cref="DocumentIndex"/> / <see cref="ComputedIndex"/>.
/// </summary>
public class Bug_4367_ngram_index_tracks_versioned_alias
{
    public class Bug4367Doc
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    [Fact]
    public void ngram_index_name_picks_up_alias_mutation_after_construction()
    {
        var mapping = DocumentMapping.For<Bug4367Doc>();
        mapping.NgramIndex(x => x.Name);

        var ngram = mapping.Indexes.OfType<NgramIndex>().Single();
        var preMutationName = ngram.Name;
        preMutationName.ShouldContain(mapping.TableName.Name);

        // Mirror what ProjectionVersionAliasPolicy.Apply does for a versioned
        // aggregate projection (Version > 1).
        mapping.Alias += "_2";

        var postMutationName = ngram.Name;
        postMutationName.ShouldContain(mapping.TableName.Name);
        postMutationName.ShouldEndWith("_2_idx_ngram_name");
        postMutationName.ShouldNotBe(preMutationName);
    }
}
