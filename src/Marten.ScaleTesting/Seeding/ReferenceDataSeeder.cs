using Marten.ScaleTesting.Domain;

namespace Marten.ScaleTesting.Seeding;

/// <summary>
/// Bulk-inserts the reference documents (Patient / Provider / RoutingReason /
/// Specialty) the composite projections enrich against. Idempotent — if the
/// expected counts are already present per tenant we skip the inserts.
/// </summary>
internal static class ReferenceDataSeeder
{
    private const int PatientsPerTenant = 200;
    private const int ProvidersPerTenant = 50;

    private static readonly (string Code, string Description, int Severity)[] s_routingReasons =
    [
        ("ROUTINE", "Routine appointment", 1),
        ("URGENT", "Urgent care needed", 5),
        ("FOLLOWUP", "Follow up from prior visit", 2),
        ("CRITICAL", "Critical / emergency", 9)
    ];

    private static readonly (string Code, string Description)[] s_specialties =
    [
        ("CARD", "Cardiology"),
        ("DERM", "Dermatology"),
        ("ENT", "Ear, Nose, Throat"),
        ("GP", "General Practice"),
        ("NEURO", "Neurology"),
        ("ORTHO", "Orthopedics"),
        ("PED", "Pediatrics"),
        ("PSYCH", "Psychiatry")
    ];

    /// <summary>
    /// Deterministic per-tenant reference data. Returns a snapshot of the
    /// generated Patient + Provider ids so the stream seeder can pick from
    /// them without re-querying.
    /// </summary>
    public static async Task<TenantReferenceData> SeedAsync(
        IDocumentStore store,
        string tenantId,
        int tenantIndex,
        int rootSeed,
        CancellationToken token)
    {
        var rng = new Random(HashCode.Combine(rootSeed, tenantIndex));

        // Specialty docs are tiny and global-ish, but conjoined tenancy means
        // every tenant gets its own row. Idempotent by code (primary key).
        await using (var session = store.LightweightSession(tenantId))
        {
            foreach (var (code, desc) in s_specialties)
            {
                session.Store(new Specialty { Code = code, Description = desc });
            }
            foreach (var (code, desc, sev) in s_routingReasons)
            {
                session.Store(new RoutingReason
                {
                    Id = Guid.NewGuid(),
                    Code = code,
                    Description = desc,
                    Severity = sev,
                    IsActive = true
                });
            }
            await session.SaveChangesAsync(token).ConfigureAwait(false);
        }

        var patients = new Guid[PatientsPerTenant];
        var providers = new Guid[ProvidersPerTenant];

        await using (var session = store.LightweightSession(tenantId))
        {
            for (var i = 0; i < PatientsPerTenant; i++)
            {
                patients[i] = Guid.NewGuid();
                session.Store(new Patient
                {
                    Id = patients[i],
                    FirstName = $"P{tenantIndex:D4}-{i:D4}-F",
                    LastName = $"P{tenantIndex:D4}-{i:D4}-L"
                });
            }
            for (var i = 0; i < ProvidersPerTenant; i++)
            {
                providers[i] = Guid.NewGuid();
                session.Store(new Provider
                {
                    Id = providers[i],
                    FirstName = $"Dr{tenantIndex:D4}-{i:D3}-F",
                    LastName = $"Dr{tenantIndex:D4}-{i:D3}-L",
                    Role = (ProviderRole)(rng.Next() % 3)
                });
            }
            await session.SaveChangesAsync(token).ConfigureAwait(false);
        }

        return new TenantReferenceData(patients, providers);
    }
}

internal sealed record TenantReferenceData(Guid[] Patients, Guid[] Providers);
