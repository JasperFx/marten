using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

#nullable enable

namespace EventSourcingTests.Aggregation
{
    #region sample_aggregate-stream-time-travelling-definition

    public enum RoomType
    {
        Single,
        Double,
        King
    }

    public record HotelRoomsDefined(
        Guid HotelId,
        Dictionary<RoomType, int> RoomTypeCounts
    );

    public record RoomBooked(
        Guid HotelId,
        RoomType RoomType
    );

    public record GuestCheckedOut(
        Guid HotelId,
        Guid GuestId,
        RoomType RoomType
    );

    public class RoomsAvailability
    {
        public Guid Id { get; private set; }

        public int AvailableSingleRooms => roomTypeCounts[RoomType.Single];
        public int AvailableDoubleRooms => roomTypeCounts[RoomType.Double];
        public int AvailableKingRooms => roomTypeCounts[RoomType.King];

        private Dictionary<RoomType, int> roomTypeCounts = new ();

        public void Apply(HotelRoomsDefined @event)
        {
            Id = @event.HotelId;
            roomTypeCounts = @event.RoomTypeCounts;
        }

        public void Apply(RoomBooked @event)
        {
            roomTypeCounts[@event.RoomType] -= 1;
        }

        public void Apply(GuestCheckedOut @event)
        {
            roomTypeCounts[@event.RoomType] += 1;
        }
    }

    #endregion

    public class time_travelling_samples: IntegrationContext
    {
        [Fact]
        public async Task TimeTravellingByDateTimeShouldReturnStateAtPointOfTime()
        {
            var hotelId = Guid.NewGuid();

            const int singleRoomsCount = 5;
            const int doubleRoomsCount = 20;
            const int kingRoomsCount = 1;

            await AppendEvent(hotelId, new HotelRoomsDefined(
                hotelId,
                new()
                {
                    { RoomType.Single, singleRoomsCount },
                    { RoomType.Double, doubleRoomsCount },
                    { RoomType.King, kingRoomsCount }
                }
            ));

            await AppendEvent(hotelId,
                new RoomBooked(
                    hotelId,
                    RoomType.Single
                ),
                new RoomBooked(
                    hotelId,
                    RoomType.Single
                ),
                new RoomBooked(
                    hotelId,
                    RoomType.Double
                ),
                new RoomBooked(
                    hotelId,
                    RoomType.King
                ));

            var pointOfTime = DateTime.UtcNow;
            var specificVersion =
                (await theSession.Events.FetchStreamStateAsync(hotelId))
                .Version;

            await AppendEvent(hotelId,
                new GuestCheckedOut(
                    hotelId,
                    Guid.NewGuid(),
                    RoomType.Single
                ),
                new GuestCheckedOut(
                    hotelId,
                    Guid.NewGuid(),
                    RoomType.King
                )
            );

            #region sample_aggregate-stream-time-travelling-by-point-of-time

            var roomsAvailabilityAtPointOfTime =
                await theSession.Events
                    .AggregateStreamAsync<RoomsAvailability>(hotelId, timestamp: pointOfTime);

            #endregion

            roomsAvailabilityAtPointOfTime.ShouldNotBeNull();
            roomsAvailabilityAtPointOfTime.Id.ShouldBe(hotelId);
            roomsAvailabilityAtPointOfTime.AvailableSingleRooms.ShouldBe(singleRoomsCount - 2);
            roomsAvailabilityAtPointOfTime.AvailableDoubleRooms.ShouldBe(doubleRoomsCount - 1);
            roomsAvailabilityAtPointOfTime.AvailableKingRooms.ShouldBe(kingRoomsCount - 1);

            #region sample_aggregate-stream-time-travelling-by-specific-version

            var roomsAvailabilityAtVersion =
                await theSession.Events
                    .AggregateStreamAsync<RoomsAvailability>(hotelId, version: specificVersion);

            #endregion

            roomsAvailabilityAtVersion.ShouldNotBeNull();
            roomsAvailabilityAtVersion.Id.ShouldBe(hotelId);
            roomsAvailabilityAtVersion.AvailableSingleRooms.ShouldBe(singleRoomsCount - 2);
            roomsAvailabilityAtVersion.AvailableDoubleRooms.ShouldBe(doubleRoomsCount - 1);
            roomsAvailabilityAtVersion.AvailableKingRooms.ShouldBe(kingRoomsCount - 1);
        }

        public time_travelling_samples(DefaultStoreFixture fixture): base(fixture)
        {
        }
    }
}
