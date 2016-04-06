using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Schema;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public interface ISmurf
    {
        string Ability { get; set; }
        Guid Id { get; set; }
    }
    public class Smurf : ISmurf
    {
        public string Ability { get; set; }
        public Guid Id { get; set; }
    }
    public class PapaSmurf : Smurf{}
    public class BrainySmurf : PapaSmurf{}

    public class query_with_inheritance : DocumentSessionFixture<NulloIdentityMap>
    {
        public query_with_inheritance()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<ISmurf>()
                    .AddSubclassHierarchy(typeof(Smurf), typeof(PapaSmurf), typeof(BrainySmurf));

                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.UpsertType = PostgresUpsertType.Legacy;
                _.Schema.For<ISmurf>().GinIndexJsonData();
            });
        }

        [Fact]
        public void get_all_subclasses_of_a_subclass()
        {
            var smurf = new Smurf {Ability = "Follow the herd"};
            var papa = new PapaSmurf {Ability = "Lead"};
            var brainy = new BrainySmurf{Ability = "Invent"};
            theSession.Store(smurf,papa,brainy);

            theSession.SaveChanges();

            theSession.Query<Smurf>().Count().ShouldBe(3);
        }

        [Fact]
        public void get_all_subclasses_of_a_subclass2()
        {
            var smurf = new Smurf {Ability = "Follow the herd"};
            var papa = new PapaSmurf {Ability = "Lead"};
            var brainy = new BrainySmurf{Ability = "Invent"};
            theSession.Store(smurf,papa,brainy);

            theSession.SaveChanges();

            theSession.Query<PapaSmurf>().Count().ShouldBe(2);
        }
    }
}