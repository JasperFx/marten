using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_3035_crazy_nested_boolean_conditions : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_3035_crazy_nested_boolean_conditions(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task behave_correctly_if_comparing_to_null_string()
    {
        var territoryId = Guid.NewGuid();
        var activeDetails = new TrackWarrantDetails
        {
            TerritoryId = territoryId,
            IsActive = true,
            IssuedDuringShiftId = "a",
            AuthorizedDuringShiftId = "a",
            VoidedDuringShiftId = "a",
            LimitsReportedClearDuringShiftId = "a"
        };

        var inactiveDetails = new TrackWarrantDetails
        {
            TerritoryId = territoryId,
            IsActive = false,
            IssuedDuringShiftId = "a",
            AuthorizedDuringShiftId = "a",
            VoidedDuringShiftId = "a",
            LimitsReportedClearDuringShiftId = "a"
        };

        var wrongShift = new TrackWarrantDetails
        {
            TerritoryId = territoryId,
            IsActive = false,
            IssuedDuringShiftId = "d",
            AuthorizedDuringShiftId = "d",
            VoidedDuringShiftId = "d",
            LimitsReportedClearDuringShiftId = "d"
        };


        var wrongTerritory = new TrackWarrantDetails
        {
            TerritoryId = Guid.NewGuid(),
            IsActive = true,
            IssuedDuringShiftId = "b",
            AuthorizedDuringShiftId = "b",
            VoidedDuringShiftId = "b",
            LimitsReportedClearDuringShiftId = "a"
        };

        theSession.Store(activeDetails, inactiveDetails, wrongShift, wrongTerritory);
        await theSession.SaveChangesAsync();



        var list = await GetTrackWarrantsForTerritory(territoryId, "a");
        list.Any(x => x.Id == activeDetails.Id).ShouldBeTrue();
        list.Any(x => x.Id == inactiveDetails.Id).ShouldBeTrue();

        theSession.Logger = new TestOutputMartenLogger(_output);

        var list2 = await GetTrackWarrantsForTerritory(territoryId, null);
        list2.Single().Id.ShouldBe(activeDetails.Id);
    }

    public Task<IReadOnlyList<TrackWarrantDetails>> GetTrackWarrantsForTerritory(
        Guid territoryId,
        string? currentShiftId) =>
        theSession.Query<TrackWarrantDetails>()
            .Where(twd => twd.TerritoryId == territoryId &&
                          (twd.IsActive || (currentShiftId != null && (
                              twd.IssuedDuringShiftId == currentShiftId ||
                              twd.AuthorizedDuringShiftId == currentShiftId ||
                              twd.VoidedDuringShiftId == currentShiftId ||
                              twd.LimitsReportedClearDuringShiftId == currentShiftId))))
            .ToListAsync();
}

public class TrackWarrantDetails
{
    public Guid Id { get; set; }
    public Guid TerritoryId { get; set; }
    public bool IsActive { get; set; }
    public string IssuedDuringShiftId { get; set; }
    public string AuthorizedDuringShiftId { get; set; }
    public string VoidedDuringShiftId { get; set; }
    public string LimitsReportedClearDuringShiftId { get; set; }
}


