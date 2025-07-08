using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CommandLine;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.MultiTenancy;

namespace Marten.Events;

public class MartenInput: NetCoreInput
{
    [Description("Advance the high water mark to the highest detected point")]
    public bool AdvanceFlag { get; set; }

    [Description("Try to correct the event progression based on the event sequence after *some* database hiccups")]
    public bool CorrectFlag { get; set; }

    [Description("Limit the operation to a single tenant if specified")]
    public string TenantIdFlag { get; set; }

    [Description("Reset all Marten data")]
    public bool ResetFlag { get; set; }
}

[Description("Advanced Marten operations to 'heal' event store projection issues or reset data")]
public class MartenCommand : JasperFxAsyncCommand<MartenInput>
{
    public override async Task<bool> Execute(MartenInput input)
    {
        using var host = input.BuildHost();
        var store = host.DocumentStore().As<DocumentStore>();

        if (input.TenantIdFlag.IsNotEmpty())
        {
            var tenantId = store.Options.TenantIdStyle.MaybeCorrectTenantId(input.TenantIdFlag);

            if (input.AdvanceFlag)
            {
                Console.WriteLine("Advancing the high water mark for tenant " + tenantId);
                await store.Advanced.AdvanceHighWaterMarkToLatestAsync(tenantId, CancellationToken.None).ConfigureAwait(false);
            }

            if (input.ResetFlag)
            {
                Console.WriteLine("Resetting the Marten data for tenant " + tenantId);
                var tenant = await store.Tenancy.GetTenantAsync(tenantId).ConfigureAwait(false);
                await tenant.Database.DeleteAllEventDataAsync(CancellationToken.None).ConfigureAwait(false);
                await tenant.Database.DeleteAllDocumentsAsync(CancellationToken.None).ConfigureAwait(false);
            }

            if (input.CorrectFlag)
            {
                Console.WriteLine("Trying to correct the event store progression for tenant " + tenantId);
                await store.Advanced.TryCorrectProgressInDatabaseAsync(tenantId, CancellationToken.None).ConfigureAwait(false);
            }
        }
        else
        {
            if (input.AdvanceFlag)
            {
                Console.WriteLine("Advancing the high water mark");
                await store.Advanced.AdvanceHighWaterMarkToLatestAsync(CancellationToken.None).ConfigureAwait(false);
            }

            if (input.ResetFlag)
            {
                Console.WriteLine("Resetting the Marten data");
                await store.Advanced.ResetAllData().ConfigureAwait(false);
            }

            if (input.CorrectFlag)
            {
                Console.WriteLine("Trying to correct the event store progression");
                await store.Advanced.TryCorrectProgressInDatabaseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        Console.WriteLine("Complete.");
        return true;
    }
}
