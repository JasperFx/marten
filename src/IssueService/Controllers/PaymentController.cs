using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Marten;
using Marten.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using StronglyTypedIds;

namespace IssueService.Controllers;

public class PaymentController : ControllerBase
{
    [HttpGet("/payment/{paymentId}")]
    public Task WritePayment(Guid paymentId, [FromServices] IQuerySession session)
    {
        return session.Json.WriteById<Payment>(new PaymentId(paymentId), HttpContext);
    }
}

[StronglyTypedId(Template.Guid)]
public readonly partial struct PaymentId;

public class Payment
{
    [JsonInclude] public PaymentId? Id { get; set; }

    [JsonInclude] public DateTimeOffset CreatedAt { get; set; }

    [JsonInclude] public PaymentState State { get; set; }


}

public enum PaymentState
{
    Created,
    Initialized,
    Canceled,
    Verified
}

