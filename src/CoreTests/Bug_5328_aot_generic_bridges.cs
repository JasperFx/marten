using System;
using System.Linq;
using System.Reflection;
using Marten;
using Marten.Internal;
using Marten.Internal.CompiledQueries;
using Marten.Internal.Sessions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests;

// #5328: a Native AOT binary threw MissingMethodException on the first document read because two
// bridges from a runtime Type back to a closed generic went through MakeGenericType +
// Activator.CreateInstance, and ILC has no native code for an instantiation nothing constructs
// statically:
//
//   QuerySession.StorageFor(Type)        -> QuerySession+StorageFinder`1[TDoc]
//   CompiledQueryPlan.sortMembers()      -> PropertyQueryMember`1[System.String]
//
// Both now resolve through registrations captured at a generic entry point. These tests assert the
// registrations happen and that the reflective fallback is genuinely unused for the shapes it
// used to serve — a JIT would happily hide a regression here, so the assertion has to be about
// which path ran, not about the result.
public class Bug_5328_aot_generic_bridges: OneOffConfigurationsContext
{
    [Fact]
    public void schema_registration_records_the_closed_generic_storage_lookup()
    {
        StoreOptions(opts => opts.Schema.For<Bug5328Registered>());

        using var session = theStore.QuerySession();

        // Schema.For<T>() is the earliest generic mention of a document type; it has to be enough
        // on its own, because the Type-keyed admin paths (document cleaner, TruncateTable) may run
        // before anything has ever queried or stored a T.
        DocumentStorageResolvers.TryResolve(typeof(Bug5328Registered), (QuerySession)session)
            .ShouldNotBeNull();
    }

    [Fact]
    public void querying_registers_the_document_type_before_storage_is_resolved()
    {
        using var session = theStore.QuerySession();

        // Nothing has touched Bug5328Queried yet.
        session.Query<Bug5328Queried>().ToString().ShouldNotBeNull();

        DocumentStorageResolvers.TryResolve(typeof(Bug5328Queried), (QuerySession)session)
            .ShouldNotBeNull();
    }

    [Fact]
    public void the_registry_and_the_reflective_shim_agree()
    {
        using var session = (QuerySession)theStore.QuerySession();

        // The generic call is what proves the instantiation exists, so it is also what registers.
        var throughTheGeneric = session.StorageFor<User>();
        var throughTheRegistry = DocumentStorageResolvers.TryResolve(typeof(User), session);

        throughTheRegistry.ShouldNotBeNull();
        throughTheRegistry.DocumentType.ShouldBe(throughTheGeneric.DocumentType);
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(DateTimeOffset))]
    [InlineData(typeof(string[]))]
    [InlineData(typeof(Guid[]))]
    [InlineData(typeof(int[]))]
    public void a_parameter_finder_owns_every_supported_compiled_query_member_type(Type memberType)
    {
        // If no finder can build the member, CompiledQueryPlan falls back to MakeGenericType and
        // the compiled query breaks under Native AOT. Each of these types must be owned by a
        // finder that is already closed over it at compile time.
        var member = typeof(Bug5328QueryShape).GetProperty(nameof(Bug5328QueryShape.Value))!;

        QueryCompiler.Finders
            .Select(finder => finder.BuildQueryMember(member, memberType))
            .ShouldContain(x => x != null);
    }

    [Fact]
    public void the_finder_built_member_matches_what_the_reflective_shim_produced()
    {
        var property = typeof(Bug5328QueryShape).GetProperty(nameof(Bug5328QueryShape.Value))!;

        var built = QueryCompiler.Finders
            .Select(finder => finder.BuildQueryMember(property, typeof(string)))
            .First(x => x != null);

        built.ShouldBeOfType<PropertyQueryMember<string>>();
        built.Type.ShouldBe(typeof(string));
        built.Member.ShouldBe((MemberInfo)property);
        built.GetValueAsObject(new Bug5328QueryShape { Value = "hello" }).ShouldBe("hello");
    }

    [Fact]
    public void fields_are_covered_too_because_compiled_queries_allow_them()
    {
        var field = typeof(Bug5328QueryShape).GetField(nameof(Bug5328QueryShape.Field))!;

        var built = QueryCompiler.Finders
            .Select(finder => finder.BuildQueryMember(field, typeof(int)))
            .First(x => x != null);

        built.ShouldBeOfType<FieldQueryMember<int>>();
        built.GetValueAsObject(new Bug5328QueryShape { Field = 11 }).ShouldBe(11);
    }

    [Fact]
    public void enum_members_are_the_one_documented_gap_and_fall_back_deliberately()
    {
        var property = typeof(Bug5328QueryShape).GetProperty(nameof(Bug5328QueryShape.Value))!;

        // No finder is closed over a consumer's enum type, so this shape still needs the
        // reflective path. Pinned so the gap is a decision rather than a surprise.
        QueryCompiler.Finders
            .Select(finder => finder.BuildQueryMember(property, typeof(Bug5328Colour)))
            .ShouldAllBe(x => x == null);
    }
}

public class Bug5328Registered
{
    public Guid Id { get; set; }
}

public class Bug5328Queried
{
    public Guid Id { get; set; }
}

public class Bug5328QueryShape
{
    public int Field;
    public string Value { get; set; }
}

public enum Bug5328Colour
{
    Red,
    Blue
}
