using System;
using System.Collections.Generic;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_2018_fts_string_list: BugIntegrationContext
{
    public sealed class BugFullTextSearchFields
    {
        public Guid Id { get; set; }
        public string Text { get; set; }
        public List<string> Data { get; set; }
    }

    public Bug_2018_fts_string_list()
    {
        StoreOptions(_ => _.Schema
            .For<BugFullTextSearchFields>()
            .GinIndexJsonData()
            .FullTextIndex(index =>
                {
                    index.Name = "mt_custom_my_index_name_fulltext_search";
                    index.RegConfig = "english";
                },
                x => x.Text,
                x => x.Data)
            .UseOptimisticConcurrency(true));
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public void can_do_index_with_full_text_search()
    {
        using var session = theStore.LightweightSession();
        session.Store(new BugFullTextSearchFields()
        {
            Id = Guid.NewGuid(),
            Text = "Hello my Darling, this is a long text",
            Data = new List<string>()
            {
                "Foo",
                "VeryLongEntry",
                "Baz"
            },
        });

        session.SaveChanges();
    }
}
