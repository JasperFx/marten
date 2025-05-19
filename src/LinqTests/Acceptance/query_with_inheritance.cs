﻿using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

#region sample_smurfs-hierarchy

public interface ISmurf
{
    string Ability { get; set; }
    Guid Id { get; set; }
}

public class Smurf: ISmurf
{
    public string Ability { get; set; }
    public Guid Id { get; set; }
}

public interface IPapaSmurf: ISmurf
{
}

public class PapaSmurf: Smurf, IPapaSmurf
{
}

public class PapySmurf: Smurf, IPapaSmurf
{
}

public class BrainySmurf: PapaSmurf
{
}

#endregion

public class sub_class_hierarchies: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public sub_class_hierarchies(ITestOutputHelper output)
    {
        _output = output;
        StoreOptions(_ =>
        {
            #region sample_add-subclass-hierarchy-with-aliases

            _.Schema.For<ISmurf>()
                .AddSubClassHierarchy(
                    typeof(Smurf),
                    new MappedType(typeof(PapaSmurf), "papa"),
                    typeof(PapySmurf),
                    typeof(IPapaSmurf),
                    typeof(BrainySmurf)
                );

            #endregion

            _.Connection(ConnectionSource.ConnectionString);
            _.AutoCreateSchemaObjects = AutoCreate.All;

            _.Schema.For<ISmurf>().GinIndexJsonData();
        });
    }

    [Fact]
    public async Task get_all_subclasses_of_a_subclass()
    {
        var smurf = new Smurf {Ability = "Follow the herd"};
        var papa = new PapaSmurf {Ability = "Lead"};
        var brainy = new BrainySmurf {Ability = "Invent"};
        theSession.Store(smurf, papa, brainy);

        await theSession.SaveChangesAsync();

        theSession.Query<Smurf>().Count().ShouldBe(3);
    }
}

public class query_with_inheritance: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    #region sample_add-subclass-hierarchy

    public query_with_inheritance(ITestOutputHelper output)
    {
        _output = output;
        StoreOptions(_ =>
        {
            _.Schema.For<ISmurf>()
                .AddSubClassHierarchy(typeof(Smurf), typeof(PapaSmurf), typeof(PapySmurf), typeof(IPapaSmurf),
                    typeof(BrainySmurf));

            // Alternatively, you can use the following:
            // _.Schema.For<ISmurf>().AddSubClassHierarchy();
            // this, however, will use the assembly
            // of type ISmurf to get all its' subclasses/implementations.
            // In projects with many types, this approach will be unadvisable.


            _.Connection(ConnectionSource.ConnectionString);
            _.AutoCreateSchemaObjects = AutoCreate.All;

            _.Schema.For<ISmurf>().GinIndexJsonData();
        });
    }

    #endregion

    [Fact]
    public async Task get_all_subclasses_of_an_interface_and_instantiate_them()
    {
        var smurf = new Smurf {Ability = "Follow the herd"};
        var papa = new PapaSmurf {Ability = "Lead"};
        var papy = new PapySmurf {Ability = "Lead"};
        var brainy = new BrainySmurf {Ability = "Invent"};
        theSession.Store(smurf, papa, brainy, papy);

        await theSession.SaveChangesAsync();

        var list = theSession.Query<IPapaSmurf>().ToList();
        list.Count().ShouldBe(3);
        list.Count(s => s.Ability == "Invent").ShouldBe(1);
    }

    [Fact]
    public async Task get_all_subclasses_of_an_interface_and_instantiate_them_async()
    {
        var smurf = new Smurf {Ability = "Follow the herd"};
        var papa = new PapaSmurf {Ability = "Lead"};
        var papy = new PapySmurf {Ability = "Lead"};
        var brainy = new BrainySmurf {Ability = "Invent"};
        theSession.Store(smurf, papa, brainy, papy);

        await theSession.SaveChangesAsync();

        var list = await theSession.Query<IPapaSmurf>().ToListAsync();
        list.Count().ShouldBe(3);
        list.Count(s => s.Ability == "Invent").ShouldBe(1);
    }

    #region sample_query-subclass-hierarchy

    [Fact]
    public async Task get_all_subclasses_of_a_subclass()
    {
        var smurf = new Smurf {Ability = "Follow the herd"};
        var papa = new PapaSmurf {Ability = "Lead"};
        var brainy = new BrainySmurf {Ability = "Invent"};
        theSession.Store(smurf, papa, brainy);

        await theSession.SaveChangesAsync();

        theSession.Query<Smurf>().Count().ShouldBe(3);
    }

    [Fact]
    public async Task get_all_subclasses_of_a_subclass2()
    {
        var smurf = new Smurf {Ability = "Follow the herd"};
        var papa = new PapaSmurf {Ability = "Lead"};
        var brainy = new BrainySmurf {Ability = "Invent"};
        theSession.Store(smurf, papa, brainy);

        await theSession.SaveChangesAsync();

        theSession.Logger = new TestOutputMartenLogger(_output);

        theSession.Query<PapaSmurf>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task get_all_subclasses_of_a_subclass_with_where()
    {
        var smurf = new Smurf {Ability = "Follow the herd"};
        var papa = new PapaSmurf {Ability = "Lead"};
        var brainy = new BrainySmurf {Ability = "Invent"};
        theSession.Store(smurf, papa, brainy);

        await theSession.SaveChangesAsync();

        theSession.Query<PapaSmurf>().Count(s => s.Ability == "Invent").ShouldBe(1);
    }

    [Fact]
    public async Task get_all_subclasses_of_a_subclass_with_where_with_camel_casing()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<ISmurf>()
                .AddSubClassHierarchy(typeof(Smurf), typeof(PapaSmurf), typeof(PapySmurf), typeof(IPapaSmurf),
                    typeof(BrainySmurf));

            // Alternatively, you can use the following:
            // _.Schema.For<ISmurf>().AddSubClassHierarchy();
            // this, however, will use the assembly
            // of type ISmurf to get all its' subclasses/implementations.
            // In projects with many types, this approach will be undvisable.

            _.UseSystemTextJsonForSerialization(EnumStorage.AsString, Casing.CamelCase);

            _.Connection(ConnectionSource.ConnectionString);
            _.AutoCreateSchemaObjects = AutoCreate.All;

            _.Schema.For<ISmurf>().GinIndexJsonData();
        });


        var smurf = new Smurf {Ability = "Follow the herd"};
        var papa = new PapaSmurf {Ability = "Lead"};
        var brainy = new BrainySmurf {Ability = "Invent"};
        theSession.Store(smurf, papa, brainy);

        await theSession.SaveChangesAsync();

        theSession.Query<PapaSmurf>().Count(s => s.Ability == "Invent").ShouldBe(1);
    }


    [Fact]
    public async Task get_all_subclasses_of_an_interface()
    {
        var smurf = new Smurf {Ability = "Follow the herd"};
        var papa = new PapaSmurf {Ability = "Lead"};
        var papy = new PapySmurf {Ability = "Lead"};
        var brainy = new BrainySmurf {Ability = "Invent"};
        theSession.Store(smurf, papa, brainy, papy);

        await theSession.SaveChangesAsync();

        theSession.Query<IPapaSmurf>().Count().ShouldBe(3);
    }

    #endregion
}
