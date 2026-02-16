using System;
using System.Threading.Tasks;
using Marten;
using Marten.AspNetCore;
using Marten.Events;
using Microsoft.AspNetCore.Mvc;

namespace IssueService.Controllers;

// Simple events for testing
public record OrderPlaced(string Description, decimal Amount);
public record OrderShipped;

// Guid-identified aggregate for inline projection
public class Order
{
    public Guid Id { get; set; }
    public string Description { get; set; }
    public decimal Amount { get; set; }
    public bool Shipped { get; set; }

    public void Apply(OrderPlaced placed)
    {
        Description = placed.Description;
        Amount = placed.Amount;
    }

    public void Apply(OrderShipped _) => Shipped = true;
}

// String-identified aggregate for inline projection
public class NamedOrder
{
    public string Id { get; set; }
    public string Description { get; set; }
    public decimal Amount { get; set; }
    public bool Shipped { get; set; }

    public void Apply(OrderPlaced placed)
    {
        Description = placed.Description;
        Amount = placed.Amount;
    }

    public void Apply(OrderShipped _) => Shipped = true;
}

public class WriteLatestController: ControllerBase
{
    #region sample_write_latest_aggregate_to_httpresponse

    [HttpGet("/order/{orderId:guid}")]
    public Task GetOrder(Guid orderId, [FromServices] IDocumentSession session)
    {
        // Streams the raw JSON of the projected aggregate to the HTTP response
        // without deserialization/serialization when the projection is stored inline
        return session.Events.WriteLatest<Order>(orderId, HttpContext);
    }

    #endregion

    #region sample_write_latest_aggregate_by_string_to_httpresponse

    [HttpGet("/named-order/{orderId}")]
    public Task GetNamedOrder(string orderId, [FromServices] IDocumentSession session)
    {
        return session.Events.WriteLatest<NamedOrder>(orderId, HttpContext);
    }

    #endregion
}
