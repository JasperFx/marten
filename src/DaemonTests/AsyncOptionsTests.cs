using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon;
using Marten.Internal.Operations;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DaemonTests;

public class AsyncOptionsTests
{
    private readonly IMartenDatabase theDatabase = Substitute.For<IMartenDatabase>();
    private readonly ShardName theName = new ShardName("Fake", "All");
    private readonly CancellationToken theToken = CancellationToken.None;

    [Fact]
    public void teardown_by_view_type_1()
    {
        var options = new AsyncOptions();
        options.DeleteViewTypeOnTeardown<Target>();
        options.DeleteViewTypeOnTeardown(typeof(User));


        var operations = Substitute.For<IProjectionStorageSession>();
        options.RegisterTeardownActions(operations);

        operations.Received().DeleteForType(typeof(Target));
        operations.Received().DeleteForType(typeof(User));
    }

    [Fact]
    public async Task determine_starting_position_if_rebuild()
    {
        var options = new AsyncOptions();
        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Rebuild, theDatabase, theToken))
            .ShouldBe(new Position(0, true));

    }

    [Fact]
    public async Task determine_starting_position_if_continuous_and_no_other_constraints()
    {
        theDatabase.ProjectionProgressFor(theName, theToken).Returns(111L);

        var options = new AsyncOptions();
        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Continuous, theDatabase, theToken))
            .ShouldBe(new Position(111L, false));
    }

    [Fact]
    public async Task subscribe_from_present()
    {
        var options = new AsyncOptions();
        options.SubscribeFromPresent();

        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Continuous, theDatabase, theToken))
            .ShouldBe(new Position(2000L, true));
    }

    [Fact]
    public async Task do_not_match_on_database_name()
    {
        theDatabase.ProjectionProgressFor(theName, theToken).Returns(111L);
        theDatabase.Identifier.Returns("One");

        var options = new AsyncOptions();
        options.SubscribeFromPresent("Two");

        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Continuous, theDatabase, theToken))
            .ShouldBe(new Position(111L, false));
    }

    [Fact]
    public async Task do_match_on_database_name()
    {
        theDatabase.ProjectionProgressFor(theName, theToken).Returns(111L);
        theDatabase.Identifier.Returns("One");
        theDatabase.StorageIdentifier.Returns("One");

        var options = new AsyncOptions();
        options.SubscribeFromPresent("One");

        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Continuous, theDatabase, theToken))
            .ShouldBe(new Position(2000L, true));
    }

    [Fact]
    public async Task subscribe_from_time_hit_with_no_prior()
    {
        theDatabase.ProjectionProgressFor(theName, theToken).Returns(0);
        theDatabase.Identifier.Returns("One");

        var subscriptionTime = (DateTimeOffset)DateTime.Today;

        theDatabase.FindEventStoreFloorAtTimeAsync(subscriptionTime, theToken).Returns(222L);

        var options = new AsyncOptions();
        options.SubscribeFromTime(subscriptionTime);

        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Continuous, theDatabase, theToken))
            .ShouldBe(new Position(222L, true));
    }

    [Fact]
    public async Task subscribe_from_time_miss_with_no_prior()
    {
        theDatabase.ProjectionProgressFor(theName, theToken).Returns(0);
        theDatabase.Identifier.Returns("One");

        var subscriptionTime = (DateTimeOffset)DateTime.Today;

        theDatabase.FindEventStoreFloorAtTimeAsync(subscriptionTime, theToken).Returns((long?)null);

        var options = new AsyncOptions();
        options.SubscribeFromTime(subscriptionTime);

        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Continuous, theDatabase, theToken))
            .ShouldBe(new Position(0, false));
    }

    [Fact]
    public async Task subscribe_from_time_hit_with_prior_lower_than_threshold()
    {
        theDatabase.ProjectionProgressFor(theName, theToken).Returns(200L);
        theDatabase.Identifier.Returns("One");

        var subscriptionTime = (DateTimeOffset)DateTime.Today;

        theDatabase.FindEventStoreFloorAtTimeAsync(subscriptionTime, theToken).Returns(222L);

        var options = new AsyncOptions();
        options.SubscribeFromTime(subscriptionTime);

        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Continuous, theDatabase, theToken))
            .ShouldBe(new Position(222L, true));
    }

    [Fact]
    public async Task subscribe_from_time_hit_with_prior_higher_than_threshold()
    {
        theDatabase.ProjectionProgressFor(theName, theToken).Returns(500L);
        theDatabase.Identifier.Returns("One");

        var subscriptionTime = (DateTimeOffset)DateTime.Today;

        theDatabase.FindEventStoreFloorAtTimeAsync(subscriptionTime, theToken).Returns(222L);

        var options = new AsyncOptions();
        options.SubscribeFromTime(subscriptionTime);

        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Continuous, theDatabase, theToken))
            .ShouldBe(new Position(500L, false));
    }

    [Fact]
    public async Task subscribe_from_time_hit_with_prior_higher_than_threshold_and_rebuild()
    {
        theDatabase.ProjectionProgressFor(theName, theToken).Returns(500L);
        theDatabase.Identifier.Returns("One");

        var subscriptionTime = (DateTimeOffset)DateTime.Today;

        theDatabase.FindEventStoreFloorAtTimeAsync(subscriptionTime, theToken).Returns(222L);

        var options = new AsyncOptions();
        options.SubscribeFromTime(subscriptionTime);

        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Rebuild, theDatabase, theToken))
            .ShouldBe(new Position(222L, true));
    }

    [Fact]
    public async Task subscribe_from_sequence_hit_with_no_prior()
    {
        theDatabase.ProjectionProgressFor(theName, theToken).Returns(100);
        theDatabase.Identifier.Returns("One");

        var options = new AsyncOptions();
        options.SubscribeFromSequence(222L);

        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Continuous, theDatabase, theToken))
            .ShouldBe(new Position(222L, true));
    }

    [Fact]
    public async Task subscribe_from_sequence_miss_with_no_prior()
    {
        theDatabase.ProjectionProgressFor(theName, theToken).Returns(0);
        theDatabase.Identifier.Returns("One");

        var options = new AsyncOptions();
        options.SubscribeFromSequence(222L);

        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Continuous, theDatabase, theToken))
            .ShouldBe(new Position(222L, true));
    }

    [Fact]
    public async Task subscribe_from_sequence_hit_with_prior_lower_than_threshold()
    {
        theDatabase.ProjectionProgressFor(theName, theToken).Returns(200L);
        theDatabase.Identifier.Returns("One");

        var options = new AsyncOptions();
        options.SubscribeFromSequence(222L);

        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Continuous, theDatabase, theToken))
            .ShouldBe(new Position(222L, true));
    }

    [Fact]
    public async Task subscribe_from_sequence_hit_with_prior_higher_than_threshold()
    {
        theDatabase.ProjectionProgressFor(theName, theToken).Returns(500L);
        theDatabase.Identifier.Returns("One");

        var options = new AsyncOptions();
        options.SubscribeFromSequence(222L);

        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Continuous, theDatabase, theToken))
            .ShouldBe(new Position(500L, false));
    }

    [Fact]
    public async Task subscribe_from_sequence_hit_with_prior_higher_than_threshold_and_rebuild()
    {
        theDatabase.ProjectionProgressFor(theName, theToken).Returns(500L);
        theDatabase.Identifier.Returns("One");

        var options = new AsyncOptions();
        options.SubscribeFromSequence(222L);

        (await options.DetermineStartingPositionAsync(2000L, theName, ShardExecutionMode.Rebuild, theDatabase, theToken))
            .ShouldBe(new Position(222L, true));
    }




}
