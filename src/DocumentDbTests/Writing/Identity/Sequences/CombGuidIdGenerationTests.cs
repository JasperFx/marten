using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

public class CombGuidIdGenerationTests : OneOffConfigurationsContext
{
    [Fact]
    public void generate_lots_of_guids()
    {
        var seed = Guid.NewGuid();

        var list = new List<Guid>();
        for (int i = 0; i < 20; i++)
        {
            list.Add(CombGuidIdGeneration.Create(seed, DateTime.UtcNow));
        }

        list.OrderBy(x => x).ShouldHaveTheSameElementsAs(list);
    }

    [Fact]
    public void When_ids_are_generated_the_first_id_should_be_less_than_the_second()
    {
        var id1 = Format(CombGuidIdGeneration.NewGuid(new DateTime(2015, 03, 31, 21, 23, 00)));
        var id2 = Format(CombGuidIdGeneration.NewGuid(new DateTime(2015, 03, 31, 21, 23, 01)));

        id1.CompareTo(id2).ShouldBe(-1);
    }

    [Fact]
    public void When_documents_are_stored_after_each_other_then_the_first_id_should_be_less_than_the_second()
    {
        StoreOptions(options =>
        {
            #region sample_configuring-global-sequentialguid
            options.Policies.ForAllDocuments(m =>
            {
                if (m.IdType == typeof(Guid))
                {
                    m.IdStrategy = new CombGuidIdGeneration();
                }
            });
            #endregion
        });


        StoreUser(theStore, "User1");
        Thread.Sleep(4); //we need some time inbetween to ensure the timepart of the CombGuid is different
        StoreUser(theStore, "User2");
        Thread.Sleep(4);
        StoreUser(theStore, "User3");

        var users = GetUsers(theStore);

        var id1 = FormatIdAsByteArrayString(users, "User1");
        var id2 = FormatIdAsByteArrayString(users, "User2");
        var id3 = FormatIdAsByteArrayString(users, "User3");

        id1.CompareTo(id2).ShouldBe(-1);
        id2.CompareTo(id3).ShouldBe(-1);
    }

    [Fact]
    public void When_CombGuid_is_defined_for_a_single_document_then_Guid_should_be_used_as_Default()
    {
        StoreOptions(options =>
        {
            #region sample_configuring-mapping-specific-sequentialguid
            options.Schema.For<UserWithGuid>().IdStrategy(new CombGuidIdGeneration());
            #endregion
        });

        theStore.StorageFeatures.MappingFor(typeof(UserWithGuid)).As<DocumentMapping>().IdStrategy.ShouldBeOfType<CombGuidIdGeneration>();
        theStore.StorageFeatures.MappingFor(typeof(UserWithGuid2)).As<DocumentMapping>().IdStrategy.ShouldBeOfType<CombGuidIdGeneration>();
    }

    [Fact]
    public void Can_Roundtrip_CombGuid_DateTimeOffset()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var comb = CombGuidIdGeneration.Create(Guid.NewGuid(), timestamp);
        var roundtrip = CombGuidIdGeneration.GetTimestamp(comb);

        roundtrip.ToUnixTimeMilliseconds().ShouldBe(timestamp.ToUnixTimeMilliseconds());
    }

    private static string FormatIdAsByteArrayString(UserWithGuid[] users, string user1)
    {
        var id = users.Single(user => user.LastName == user1).Id;
        return Format(id);
    }

    private static string Format(Guid id)
    {
        return id.ToString();
    }

    private static UserWithGuid[] GetUsers(IDocumentStore documentStore)
    {
        using var session = documentStore.QuerySession();
        return session.Query<UserWithGuid>().ToArray();
    }

    private static void StoreUser(IDocumentStore documentStore, string lastName)
    {
        using var session = documentStore.IdentitySession();
        session.Store(new UserWithGuid { LastName = lastName });
        session.SaveChanges();
    }


}
