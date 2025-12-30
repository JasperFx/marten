using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bogus;
using DaemonTests.TeleHealth;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Descriptors;
using Marten;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Composites;

public class multi_stage_projections: DaemonContext
{
    protected readonly List<Guid> theBoards = new();
    protected readonly List<Patient> thePatients = new();
    protected readonly List<Provider> theProviders = new();

    public multi_stage_projections(ITestOutputHelper output): base(output)
    {
    }

    private async Task buildSpecialties()
    {
        theSession.Store(new Specialty { Code = "PED", Description = "Pediatrics" });
        theSession.Store(new Specialty { Code = "GEN", Description = "General Practice" });
        theSession.Store(new Specialty { Code = "ENT", Description = "Ear, Nose, and Throat" });
        theSession.Store(new Specialty { Code = "ORTH", Description = "Orthopedics" });

        await theSession.SaveChangesAsync();
    }

    protected async Task buildProviders()
    {
        var faker = new Faker<Provider>()
            .StrictMode(false)
            .RuleFor(x => x.FirstName, f => f.Name.FirstName())
            .RuleFor(x => x.LastName, f => f.Name.LastName())
            .RuleFor(x => x.Licensing,
                f => [new Licensing(f.PickRandom("ORTH", "ENT", "GEN", "PED"), f.PickRandom("TX", "OK", "AR"))])
            .RuleFor(x => x.Role, f => f.PickRandom<ProviderRole>());

        // TODO -- replace w/ Bogus some day
        for (var i = 0; i < 100; i++)
        {
            theProviders.Add(faker.Generate());
        }

        await theStore.BulkInsertAsync(theProviders);
    }

    protected async Task startBoards()
    {
        var specialties = await theSession.Query<Specialty>().ToListAsync();
        foreach (var specialty in specialties)
        {
            var tx = new BoardOpened($"Texas {specialty.Description}", DateOnly.FromDateTime(DateTime.Today),
                DateTimeOffset.UtcNow,
                ["TX"], [specialty.Code]);

            var ar = new BoardOpened($"Texas {specialty.Description}", DateOnly.FromDateTime(DateTime.Today),
                DateTimeOffset.UtcNow,
                ["AR"], [specialty.Code]);

            var ok = new BoardOpened($"Texas {specialty.Description}", DateOnly.FromDateTime(DateTime.Today),
                DateTimeOffset.UtcNow,
                ["AR"], [specialty.Code]);


            theBoards.Add(theSession.Events.StartStream<Board>(tx).Id);
            theBoards.Add(theSession.Events.StartStream<Board>(ar).Id);
            theBoards.Add(theSession.Events.StartStream<Board>(ok).Id);
        }

        await theSession.SaveChangesAsync();
    }

    protected async Task<List<Board>> fetchOpenBoards()
    {
        var list = new List<Board>();
        foreach (var guid in theBoards) list.Add(await theSession.Events.FetchLatest<Board>(guid));

        return list;
    }

    protected async Task startShifts()
    {
        var providers = await theSession.Query<Provider>().ToListAsync();
        var boards = await fetchOpenBoards();

        foreach (var provider in providers)
        {
            var board = boards.FirstOrDefault(board => provider.Licensing.Any(x =>
                board.StateCodes.Contains(x.StateCode) && board.SpecialtyCodes.Contains(x.SpecialtyCode)));

            if (board != null)
            {
                if (Random.Shared.NextDouble() < .2)
                {
                    theSession.Events.StartStream<ProviderShift>(new ProviderJoined(board.Id, provider.Id));
                }
                else
                {
                    theSession.Events.StartStream<ProviderShift>(new ProviderJoined(board.Id, provider.Id),
                        new ProviderReady());
                }
            }
        }

        await theSession.SaveChangesAsync();
    }

    protected async Task buildPatients()
    {
        var faker = new Faker<Patient>()
            .StrictMode(false)
            .RuleFor(x => x.FirstName, f => f.Name.FirstName())
            .RuleFor(x => x.LastName, f => f.Name.LastName());

        for (var i = 0; i < 100; i++)
        {
            thePatients.Add(faker.Generate());
        }

        await theStore.BulkInsertAsync(thePatients);
    }

    private async Task startAppointments()
    {
        var patients = await theSession.Query<Patient>().ToListAsync();
        var specialties = await theSession.Query<Specialty>().Select(x => x.Code).ToListAsync();
        var states = new[] { "TX", "AR", "OK" };

        var faker = new Faker();

        var boards = await theSession.Query<Board>().ToListAsync();

        foreach (var patient in patients)
        {
            var requested = new AppointmentRequested(patient.Id, faker.PickRandom(states),
                faker.PickRandom<string>(specialties));

            var board = boards.FirstOrDefault(x =>
                x.StateCodes.Contains(requested.StateCode) && x.SpecialtyCodes.Contains(requested.SpecialtyCode));

            if (board != null)
            {
                theSession.Events.StartStream<Appointment>(requested, new AppointmentRouted(board.Id));
            }
            else
            {
                theSession.Events.StartStream<Appointment>(requested);
            }
        }

        await theSession.SaveChangesAsync();
    }

    private async Task setUpData()
    {
        await buildSpecialties();
        await startBoards();
        await buildPatients();
        await buildProviders();

        await startShifts();
    }

    [Fact]
    public async Task end_to_end()
    {
        StoreOptions(opts =>
        {
            opts.Projections.CompositeProjectionFor("TeleHealth", projection =>
            {
                projection.Add<ProviderShiftProjection>();
                projection.Add<AppointmentProjection>();
                projection.Snapshot<Board>();

                // 2nd stage projections
                projection.Add<AppointmentDetailsProjection>(2);
                projection.Add<BoardSummaryProjection>(2);
            });
        });

        var usage = await theStore.As<IEventStore>().TryCreateUsage(CancellationToken.None);
        verifyDescription(usage);

        // Verifying that there is the right tables in the script
        var sql = theStore.Storage.ToDatabaseScript();
        sql.ShouldContain("mt_doc_board");
        sql.ShouldContain("mt_doc_boardsummary");

        await setUpData();

        using var daemon = await StartDaemon();
        await daemon.StartAllAsync();

        await daemon.WaitForNonStaleData(30.Seconds());

        // All the Boards exist
        (await theSession.Query<Board>().CountAsync()).ShouldBe(12);

        // Built up ProviderShifts
        (await theSession.Query<ProviderShift>().CountAsync()).ShouldBeGreaterThan(0);

        await startAppointments();
        await daemon.WaitForNonStaleData(30.Seconds());

        // Got appointments
        (await theSession.Query<Appointment>().CountAsync()).ShouldBeGreaterThan(0);

        // Got details from the 2nd stage projection!
        (await theSession.Query<AppointmentDetails>().CountAsync()).ShouldBeGreaterThan(0);

        // See the downstream BoardSummary too!
        (await theSession.Query<BoardSummary>().CountAsync()).ShouldBeGreaterThan(0);
        foreach (var boardSummary in await theSession.Query<BoardSummary>().ToListAsync())
        {
            boardSummary.Board.ShouldNotBeNull();
        }

        var summaries = await theSession.QueryForNonStaleData<BoardSummary>(10.Seconds()).ToListAsync();
        summaries.Count.ShouldBe(12);

        // assign some appointments to providers
        // add the board summary
        await assignProvidersToAppointments();
        await daemon.WaitForNonStaleData(30.Seconds());
        await daemon.StopAllAsync();

        await daemon.RebuildProjectionAsync("TeleHealth", CancellationToken.None);


    }

    private static void verifyDescription(EventStoreUsage usage)
    {
        usage.ShouldNotBeNull();
        var description = usage.Subscriptions.Single();

        description.SubscriptionType.ShouldBe(SubscriptionType.CompositeProjection);
        description.Name.ShouldBe("TeleHealth");
        description.Sets.Single().Key.ShouldBe("Stages");
        var stages = description.Sets["Stages"];
        stages.Rows.Count.ShouldBe(2);


    }

    private async Task assignProvidersToAppointments()
    {
        var boards = await theSession.Query<BoardSummary>().ToListAsync();

        foreach (var board in boards)
        {
            var appointment = board.Unassigned.Values.FirstOrDefault();
            if (appointment != null)
            {
                var provider = board.ActiveProviders.Values.Where(x => x.Status == ProviderStatus.Ready).FirstOrDefault();
                if (provider != null)
                {
                    theSession.Events.Append(provider.Id, new AppointmentAssigned(appointment.Id));
                    theSession.Events.Append(appointment.Id, new ProviderAssigned(provider.ProviderId));
                }
            }
        }

        await theSession.SaveChangesAsync();
    }
}
