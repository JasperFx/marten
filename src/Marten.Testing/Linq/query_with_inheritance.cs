using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    // SAMPLE: smurfs-hierarchy
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
    public interface IPapaSmurf : ISmurf{}
    public class PapaSmurf : Smurf, IPapaSmurf{}
    public class PapySmurf : Smurf, IPapaSmurf{}
    public class BrainySmurf : PapaSmurf{ }
    // ENDSAMPLE

    public class query_with_inheritance_and_aliases : DocumentSessionFixture<NulloIdentityMap>
    {
        public query_with_inheritance_and_aliases()
        {
            StoreOptions(_ =>
            {
                // SAMPLE: add-subclass-hierarchy-with-aliases
                _.Schema.For<ISmurf>()
                    .AddSubClassHierarchy(
                        typeof(Smurf), 
                        new MappedType(typeof(PapaSmurf), "papa"), 
                        typeof(PapySmurf), 
                        typeof(IPapaSmurf), 
                        typeof(BrainySmurf)
                    );
                // ENDSAMPLE

                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Schema.For<ISmurf>().GinIndexJsonData();
            });
        }

        [Fact]
        public void get_all_subclasses_of_a_subclass()
        {
            var smurf = new Smurf { Ability = "Follow the herd" };
            var papa = new PapaSmurf { Ability = "Lead" };
            var brainy = new BrainySmurf { Ability = "Invent" };
            theSession.Store(smurf, papa, brainy);

            theSession.SaveChanges();

            theSession.Query<Smurf>().Count().ShouldBe(3);
        }
    }

    public class query_with_inheritance : DocumentSessionFixture<NulloIdentityMap>
    {
        // SAMPLE: add-subclass-hierarchy
        public query_with_inheritance()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<ISmurf>()
                    .AddSubClassHierarchy(typeof(Smurf), typeof(PapaSmurf), typeof(PapySmurf), typeof(IPapaSmurf), typeof(BrainySmurf));

                // Alternatively, you can use the following:
                // _.Schema.For<ISmurf>().AddSubClassHierarchy();
                // this, however, will use the assembly
                // of type ISmurf to get all its' subclasses/implementations. 
                // In projects with many types, this approach will be undvisable.


                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Schema.For<ISmurf>().GinIndexJsonData();
            });
        }
        // ENDSAMPLE

        // SAMPLE: query-subclass-hierarchy
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

        [Fact]
        public void get_all_subclasses_of_a_subclass_with_where()
        {
            var smurf = new Smurf {Ability = "Follow the herd"};
            var papa = new PapaSmurf {Ability = "Lead"};
            var brainy = new BrainySmurf{Ability = "Invent"};
            theSession.Store(smurf,papa,brainy);

            theSession.SaveChanges();

            theSession.Query<PapaSmurf>().Count(s=>s.Ability == "Invent").ShouldBe(1);
        }

        [Fact]
        public void get_all_subclasses_of_a_subclass_with_where_with_camel_casing()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<ISmurf>()
                    .AddSubClassHierarchy(typeof(Smurf), typeof(PapaSmurf), typeof(PapySmurf), typeof(IPapaSmurf), typeof(BrainySmurf));

                // Alternatively, you can use the following:
                // _.Schema.For<ISmurf>().AddSubClassHierarchy();
                // this, however, will use the assembly
                // of type ISmurf to get all its' subclasses/implementations. 
                // In projects with many types, this approach will be undvisable.

                _.UseDefaultSerialization(EnumStorage.AsString, Casing.CamelCase);

                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Schema.For<ISmurf>().GinIndexJsonData();
            });


            var smurf = new Smurf { Ability = "Follow the herd" };
            var papa = new PapaSmurf { Ability = "Lead" };
            var brainy = new BrainySmurf { Ability = "Invent" };
            theSession.Store(smurf, papa, brainy);

            theSession.SaveChanges();

            theSession.Query<PapaSmurf>().Count(s => s.Ability == "Invent").ShouldBe(1);
        }


        [Fact]
        public void get_all_subclasses_of_an_interface()
        {
            var smurf = new Smurf { Ability = "Follow the herd" };
            var papa = new PapaSmurf { Ability = "Lead" };
            var papy = new PapySmurf { Ability = "Lead" };
            var brainy = new BrainySmurf { Ability = "Invent" };
            theSession.Store(smurf, papa, brainy, papy);

            theSession.SaveChanges();

            theSession.Query<IPapaSmurf>().Count().ShouldBe(3);
        }
        // ENDSAMPLE

        [Fact]
        public void get_all_subclasses_of_an_interface_and_instantiate_them()
        {
            var smurf = new Smurf { Ability = "Follow the herd" };
            var papa = new PapaSmurf { Ability = "Lead" };
            var papy = new PapySmurf { Ability = "Lead" };
            var brainy = new BrainySmurf { Ability = "Invent" };
            theSession.Store(smurf, papa, brainy, papy);

            theSession.SaveChanges();

            var list = theSession.Query<IPapaSmurf>().ToList();
            list.Count().ShouldBe(3);
            list.Count(s => s.Ability == "Invent").ShouldBe(1);
        }

        [Fact]
        public async Task get_all_subclasses_of_an_interface_and_instantiate_them_async()
        {
            var smurf = new Smurf { Ability = "Follow the herd" };
            var papa = new PapaSmurf { Ability = "Lead" };
            var papy = new PapySmurf { Ability = "Lead" };
            var brainy = new BrainySmurf { Ability = "Invent" };
            theSession.Store(smurf, papa, brainy, papy);

            await theSession.SaveChangesAsync().ConfigureAwait(false);

            var list = await theSession.Query<IPapaSmurf>().ToListAsync().ConfigureAwait(false);
            list.Count().ShouldBe(3);
            list.Count(s => s.Ability == "Invent").ShouldBe(1);
        }

    }
}