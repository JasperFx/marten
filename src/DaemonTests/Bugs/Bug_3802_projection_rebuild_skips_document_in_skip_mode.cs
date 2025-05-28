using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

public class Bug_3802_projection_rebuild_skips_document_in_quick_mode: BugIntegrationContext
{
    [Fact]
    public async Task should_rebuild_all_documents()
    {
        StoreOptions(_ =>
        {
            _.Events.AppendMode = EventAppendMode.Quick;

            _.Projections.Add<CompanyProjection>(ProjectionLifecycle.Async);
            _.Projections.Add<CompanyUniqueEmailProjection>(ProjectionLifecycle.Inline);
        });

        theSession.Events.StartStream<Company>(
            new CompanyCreated("Microsoft", "info@microsoft.com"),
            new CompanyNameChanged("Microsoft Inc."),
            new CompanyNameChanged("Microsoft Ltd."));

        theSession.Events.StartStream<Company>(
            new CompanyCreated("Apple", "sales@apple.com"));

        theSession.Events.StartStream<Company>(
            new CompanyCreated("JasperFx", "sales@jasperfx.com"));

        await theSession.SaveChangesAsync();

        // Let the daemon run so the async CompanyProjection can catch up
        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(10.Seconds());
        }

        // Just making sure we have everything as expected
        var allCompanies = await theSession.Query<Company>().ToListAsync();
        allCompanies.Count.ShouldBe(3);
        var allCompanyEmails = await theSession.Query<CompanyUniqueEmail>().ToListAsync();
        allCompanyEmails.Count.ShouldBe(3);

        // Now comes the fun - rebuilding the projection
        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.RebuildProjectionAsync<CompanyUniqueEmailProjection>(CancellationToken.None);

            await daemon.WaitForNonStaleData(10.Seconds());
        }

        // Count should be 3 again - but is it?
        var allCompanyEmailsAfterRebuild = await theSession.Query<CompanyUniqueEmail>().ToListAsync();
        allCompanyEmailsAfterRebuild.Count.ShouldBe(3);
    }
}

public record CompanyCreated(string Name, string Email);

public record CompanyNameChanged(string NewName);

public class Company
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}

public class CompanyProjection: SingleStreamProjection<Company, Guid>
{
    public Company Create(CompanyCreated e)
    {
        return new Company { Name = e.Name, Email = e.Email };
    }

    public void Apply(CompanyNameChanged e, Company company)
    {
        company.Name = e.NewName;
    }
}

public class CompanyUniqueEmail
{
    public Guid Id { get; set; }
    public string Email { get; set; }
}

public class CompanyUniqueEmailProjection: SingleStreamProjection<CompanyUniqueEmail, Guid>
{
    public CompanyUniqueEmail Create(CompanyCreated e)
    {
        return new CompanyUniqueEmail { Email = e.Email.ToLowerInvariant() };
    }
}
