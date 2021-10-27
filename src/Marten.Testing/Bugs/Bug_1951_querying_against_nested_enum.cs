using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Bugs
{
    public class Bug_1951_querying_against_nested_enum : BugIntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public Bug_1951_querying_against_nested_enum(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task can_query_against_the_nested_enum()
        {
            theSession.Logger = new TestOutputMartenLogger(_output);
            theSession.Store(new TestDoc
            {
                Nested = new Nested(Guid.NewGuid(), Scope.Periscope)
            });

            theSession.Store(new TestDoc
            {
                Nested = new Nested(Guid.NewGuid(), Scope.Microscope)
            });

            await theSession.SaveChangesAsync();

            var results = await theSession.Query<TestDoc>()
                .Where(x => x.Nested.Scope == Scope.Periscope)
                .ToListAsync();

            results.Count.ShouldBe(1);
        }

        [Fact]
        public async Task can_query_against_the_nested_enum_stored_as_string()
        {
            StoreOptions(opts =>
            {
                opts.UseDefaultSerialization(EnumStorage.AsString);
            });

            theSession.Logger = new TestOutputMartenLogger(_output);
            theSession.Store(new TestDoc
            {
                Nested = new Nested(Guid.NewGuid(), Scope.Periscope)
            });

            theSession.Store(new TestDoc
            {
                Nested = new Nested(Guid.NewGuid(), Scope.Microscope)
            });

            await theSession.SaveChangesAsync();

            var results = await theSession.Query<TestDoc>()
                .Where(x => x.Nested.Scope == Scope.Periscope)
                .ToListAsync();

            results.Count.ShouldBe(1);
        }
    }

    public class TestDoc
    {
        public Guid Id { get; set; }

        public Nested Nested { get; set; }
    }

    public class Nested
    {
        public Nested(Guid id, Scope scope)
        {
            Id = id;
            Scope = scope;
            IntScope = (int)scope;
        }

        public Guid Id { get; set; }
        public Scope Scope { get; set; }
        public int IntScope { get; set; }
    }

    public enum Scope
    {
        Periscope,
        Telescope,
        Microscope
    }
}
