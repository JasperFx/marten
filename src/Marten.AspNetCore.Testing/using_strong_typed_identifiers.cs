using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Alba;
using IssueService.Controllers;
using Shouldly;
using StronglyTypedIds;
using Xunit;

namespace Marten.AspNetCore.Testing;

[Collection("integration")]
public class using_strong_typed_identifiers : IntegrationContext
{
    public using_strong_typed_identifiers(AppFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task stream_json_hit()
    {
        var payment = new Payment
        {
            CreatedAt = DateTime.Today,
            State = PaymentState.Created
        };

        using var session = Host.DocumentStore().LightweightSession();
        session.Store(payment);
        await session.SaveChangesAsync();

        var json = await session.Json.FindByIdAsync<Payment>(payment.Id);
        json.ShouldContain(payment.Id.ToString());

        var result = await Host.Scenario(s =>
        {
            s.Get.Url($"/payment/{payment.Id}");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var read = result.ReadAsJson<Payment>();

        read.State.ShouldBe(payment.State);
    }
}


