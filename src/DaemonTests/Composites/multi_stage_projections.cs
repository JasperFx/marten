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
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Storage;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Composites;

public class multi_stage_projections(ITestOutputHelper output): DaemonContext(output)
{
    private readonly List<Guid> theBoards = [];
    private readonly List<Patient> thePatients = [];
    private List<RoutingReason> theRoutingReasons = [];
    private readonly List<Provider> theProviders = [];
    private IDocumentSession _compositeSession;

    private async Task buildSpecialties()
    {
        _compositeSession.Store(new Specialty { Code = "PED", Description = "Pediatrics" });
        _compositeSession.Store(new Specialty { Code = "GEN", Description = "General Practice" });
        _compositeSession.Store(new Specialty { Code = "ENT", Description = "Ear, Nose, and Throat" });
        _compositeSession.Store(new Specialty { Code = "ORTH", Description = "Orthopedics" });

        await _compositeSession.SaveChangesAsync();
    }

    protected async Task buildProviders(string tenantId)
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

        await theStore.BulkInsertAsync(tenantId, theProviders);
    }

    protected async Task startBoards()
    {
        var specialties = await _compositeSession.Query<Specialty>().ToListAsync();
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


            theBoards.Add(_compositeSession.Events.StartStream<Board>(tx).Id);
            theBoards.Add(_compositeSession.Events.StartStream<Board>(ar).Id);
            theBoards.Add(_compositeSession.Events.StartStream<Board>(ok).Id);
        }

        await _compositeSession.SaveChangesAsync();
    }

    protected async Task<List<Board>> fetchOpenBoards()
    {
        var list = new List<Board>();
        foreach (var guid in theBoards) list.Add(await _compositeSession.Events.FetchLatest<Board>(guid));

        return list;
    }

    protected async Task startShifts()
    {
        var providers = await _compositeSession.Query<Provider>().ToListAsync();
        var boards = await fetchOpenBoards();

        foreach (var provider in providers)
        {
            var board = boards.FirstOrDefault(board => provider.Licensing.Any(x =>
                board.StateCodes.Contains(x.StateCode) && board.SpecialtyCodes.Contains(x.SpecialtyCode)));

            if (board != null)
            {
                if (Random.Shared.NextDouble() < .2)
                {
                    _compositeSession.Events.StartStream<ProviderShift>(new ProviderJoined(board.Id, provider.Id));
                }
                else
                {
                    _compositeSession.Events.StartStream<ProviderShift>(new ProviderJoined(board.Id, provider.Id),
                        new ProviderReady());
                }
            }
        }

        await _compositeSession.SaveChangesAsync();
    }

    protected async Task buildPatients(string tenantId)
    {
        var faker = new Faker<Patient>()
            .StrictMode(false)
            .RuleFor(x => x.FirstName, f => f.Name.FirstName())
            .RuleFor(x => x.LastName, f => f.Name.LastName());

        for (var i = 0; i < 100; i++)
        {
            thePatients.Add(faker.Generate());
        }

        await theStore.BulkInsertAsync(tenantId, thePatients);
    }

    private async Task BuildRoutingReasons(string tenantId)
    {
        theRoutingReasons =
        [
            new()
            {
                Id = Guid.NewGuid(),
                Code = "INCOMPLETE_RECORD",
                Description = "Required data is missing from the record",
                IsActive = true,
                Severity = 2
            },

            new()
            {
                Id = Guid.NewGuid(),
                Code = "INVALID_REFERRAL",
                Description = "The referral does not meet the required criteria",
                IsActive = true,
                Severity = 3
            },

            new()
            {
                Id = Guid.NewGuid(),
                Code = "TECHNICAL_ERROR",
                Description = "A technical error occurred during processing",
                IsActive = true,
                Severity = 4
            },

            new()
            {
                Id = Guid.NewGuid(),
                Code = "DUPLICATE_REQUEST",
                Description = "The request has already been received",
                IsActive = true,
                Severity = 1
            }
        ];

        await theStore.BulkInsertAsync(tenantId, theRoutingReasons);
    }

    private async Task startAppointments()
    {
        var patients = await _compositeSession.Query<Patient>().ToListAsync();
        var specialties = await _compositeSession.Query<Specialty>().Select(x => x.Code).ToListAsync();
        var states = new[] { "TX", "AR", "OK" };

        var faker = new Faker();

        var boards = await _compositeSession.Query<Board>().ToListAsync();

        var i = 0;
        foreach (var patient in patients)
        {
            var requested = new AppointmentRequested(patient.Id, faker.PickRandom(states),
                faker.PickRandom<string>(specialties));

            var board = boards.FirstOrDefault(x =>
                x.StateCodes.Contains(requested.StateCode) && x.SpecialtyCodes.Contains(requested.SpecialtyCode));

            var appointmentId = Guid.NewGuid();
            List<object> events = [requested];
            if (i % 2 == 0)
            {
                events.Add(new AppointmentExternalIdentifierAssigned(appointmentId, Guid.NewGuid()));
            }
            if (board != null)
            {
                var random = new Random();
                var routingReason = theRoutingReasons[random.Next(theRoutingReasons.Count)];
                _compositeSession.Events.StartStream<Appointment>(requested, new AppointmentRouted(board.Id, routingReason.Code));
            }
            else
            {
                _compositeSession.Events.StartStream<Appointment>(requested);
            }

            _compositeSession.Events.StartStream<Appointment>(appointmentId, events);

            i++;
        }

        await _compositeSession.SaveChangesAsync();
    }

    private async Task setUpData(string tenantId)
    {
        await buildSpecialties();
        await startBoards();
        await buildPatients(tenantId);
        await BuildRoutingReasons(tenantId);
        await buildProviders(tenantId);

        await startShifts();
    }

    [Theory]
    [InlineData(TenancyStyle.Single)]
    [InlineData(TenancyStyle.Conjoined)]
    public async Task end_to_end(TenancyStyle tenancyStyle)
    {
        StoreOptions(opts =>
        {
            #region sample_defining_a_composite_projection

            if(tenancyStyle == TenancyStyle.Conjoined)
            {
                opts.Events.TenancyStyle = TenancyStyle.Conjoined;
                opts.Policies.AllDocumentsAreMultiTenantedWithPartitioning(x =>
                {
                    x.ByHash(Enumerable.Range(1, 2).Select(i => $"b_{i}").ToArray());
                });
                opts.Advanced.DefaultTenantUsageEnabled = false;
            }
            opts.Projections.CompositeProjectionFor("TeleHealth", projection =>
            {
                projection.Add<ProviderShiftProjection>();
                projection.Add<AppointmentProjection>();
                projection.Snapshot<Board>();

                // 2nd stage projections
                projection.Add<AppointmentDetailsProjection>(2);
                projection.Add<BoardSummaryProjection>(2);
                projection.Add<AppointmentByExternalIdentifierProjection>(2);
            });

            #endregion
        });
        var tenantId = tenancyStyle == TenancyStyle.Conjoined ? "some_tenant" : TenantId.DefaultTenantId;
        _compositeSession = theStore.LightweightSession(tenantId);
        var usage = await theStore.As<IEventStore>().TryCreateUsage(CancellationToken.None);
        verifyDescription(usage);

        // Verifying that there is the right tables in the script
        var sql = theStore.Storage.ToDatabaseScript();
        sql.ShouldContain("mt_doc_board");
        sql.ShouldContain("mt_doc_boardsummary");

        await setUpData(tenantId);

        using var daemon = await StartDaemon();
        await daemon.StartAllAsync();

        await daemon.WaitForNonStaleData(30.Seconds());

        // All the Boards exist
        (await _compositeSession.Query<Board>().CountAsync()).ShouldBe(12);

        // Built up ProviderShifts
        (await _compositeSession.Query<ProviderShift>().CountAsync()).ShouldBeGreaterThan(0);

        await startAppointments();
        await daemon.WaitForNonStaleData(30.Seconds());

        // Got appointments
        (await _compositeSession.Query<Appointment>().CountAsync()).ShouldBeGreaterThan(0);

        // Got details from the 2nd stage projection!
        (await _compositeSession.Query<AppointmentDetails>().CountAsync()).ShouldBeGreaterThan(0);
        (await _compositeSession.Query<AppointmentByExternalIdentifier>().CountAsync()).ShouldBeGreaterThan(0);

        (await _compositeSession.Query<AppointmentDetails>().Where(x => x.RoutingReasonDescription != null).AnyAsync()).ShouldBeTrue();

        // See the downstream BoardSummary too!
        (await _compositeSession.Query<BoardSummary>().CountAsync()).ShouldBeGreaterThan(0);
        foreach (var boardSummary in await _compositeSession.Query<BoardSummary>().ToListAsync())
        {
            boardSummary.Board.ShouldNotBeNull();
        }

        #region sample_querying_for_non_stale_projection_data

        // _compositeSession is an IDocumentSession
        var summaries = await _compositeSession
            // This makes Marten "wait" until the async daemon progress for whatever projection
            // is building the BoardSummary document to catch up to the point at which the
            // event store was at when you first tired to execute the LINQ query
            .QueryForNonStaleData<BoardSummary>(10.Seconds())
            .ToListAsync();

        #endregion

        summaries.Count.ShouldBe(12);

        // assign some appointments to providers
        // add the board summary
        await assignProvidersToAppointments();
        await daemon.WaitForNonStaleData(30.Seconds());
        await daemon.StopAllAsync();

        await daemon.RebuildProjectionAsync("TeleHealth", CancellationToken.None);


        await daemon.StartAllAsync();
        // Now, let's cancel an appointment and see that AppointmentDetails is also deleted
        var appointmentId = (await _compositeSession.Query<Appointment>().FirstAsync()).Id;

        _compositeSession.Events.Append(appointmentId, new AppointmentCancelled());
        await _compositeSession.SaveChangesAsync();

        await theStore.WaitForNonStaleProjectionDataAsync(5.Seconds());

        (await _compositeSession.LoadAsync<Appointment>(appointmentId)).ShouldBeNull();
        (await _compositeSession.LoadAsync<AppointmentDetails>(appointmentId)).ShouldBeNull();



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
        var boards = await _compositeSession.Query<BoardSummary>().ToListAsync();

        foreach (var board in boards)
        {
            var appointment = board.Unassigned.Values.FirstOrDefault();
            if (appointment != null)
            {
                var provider = board.ActiveProviders.Values.Where(x => x.Status == ProviderStatus.Ready).FirstOrDefault();
                if (provider != null)
                {
                    _compositeSession.Events.Append(provider.Id, new AppointmentAssigned(appointment.Id));
                    _compositeSession.Events.Append(appointment.Id, new ProviderAssigned(provider.ProviderId));
                }
            }
        }

        await _compositeSession.SaveChangesAsync();
    }
}
