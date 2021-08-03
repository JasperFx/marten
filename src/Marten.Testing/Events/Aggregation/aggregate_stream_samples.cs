using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Aggregation
{

    #region sample_aggregate-stream-events
    public record InvoiceInitiated(
        Guid InvoiceId,
        double Amount,
        string Number,
        Person IssuedTo,
        DateTime InitiatedAt
    );

    public record Person(
        string Name,
        string Address
    );

    public record InvoiceIssued(
        Guid InvoiceId,
        string IssuedBy,
        DateTime IssuedAt
    );

    public enum InvoiceSendMethod
    {
        Email,
        Post
    }

    public record InvoiceSent(
        Guid InvoiceId,
        InvoiceSendMethod SentVia,
        DateTime SentAt
    );
    #endregion sample_aggregate-stream-events

    #region sample_aggregate-stream-invoice-entity
    public class Invoice
    {
        public Guid Id { get; private set; }
        public double Amount { get; private set; }
        public string Number { get; private set; } = default!;

        public InvoiceStatus Status { get; private set; }

        public Person IssuedTo { get; private set; } = default!;
        public DateTime InitiatedAt { get; private set; }

        public string? IssuedBy { get; private set; }
        public DateTime IssuedAt { get; private set; }

        public InvoiceSendMethod SentVia { get; private set; }
        public DateTime SentAt { get; private set; }

        public void Apply(InvoiceInitiated @event)
        {
            Id = @event.InvoiceId;
            Amount = @event.Amount;
            Number = @event.Number;
            IssuedTo = @event.IssuedTo;
            InitiatedAt = @event.InitiatedAt;
            Status = InvoiceStatus.Initiated;
        }

        public void Apply(InvoiceIssued @event)
        {
            IssuedBy = @event.IssuedBy;
            IssuedAt = @event.IssuedAt;
            Status = InvoiceStatus.Issued;
        }

        public void Apply(InvoiceSent @event)
        {
            SentVia = @event.SentVia;
            SentAt = @event.SentAt;
            Status = InvoiceStatus.Sent;
        }
    }

    public enum InvoiceStatus
    {
        Initiated = 1,
        Issued = 2,
        Sent = 3
    }

    #endregion sample_aggregate-stream-invoice-entity

    public class aggregate_stream_samples: IntegrationContext
    {
        [Fact]
        public async Task AggregationWithWhenShouldGetTheCurrentState()
        {
            var invoiceId = Guid.NewGuid();

            var invoiceInitiated = new InvoiceInitiated(
                invoiceId,
                34.12,
                "INV/2021/11/01",
                new Person("Oscar the Grouch", "123 Sesame Street"),
                DateTime.UtcNow
            );
            var invoiceIssued = new InvoiceIssued(
                invoiceId,
                "Cookie Monster",
                DateTime.UtcNow
            );
            var invoiceSent = new InvoiceSent(
                invoiceId,
                InvoiceSendMethod.Email,
                DateTime.UtcNow
            );

            theSession.Events.Append(invoiceId, invoiceInitiated, invoiceIssued, invoiceSent);
            await theSession.SaveChangesAsync();

            #region sample_aggregate-stream-usage
            var invoice = await theSession.Events.AggregateStreamAsync<Invoice>(invoiceId);
            #endregion sample_aggregate-stream-usage

            invoice.ShouldNotBeNull();
            invoice.Id.ShouldBe(invoiceInitiated.InvoiceId);
            invoice.Amount.ShouldBe(invoiceInitiated.Amount);
            invoice.Number.ShouldBe(invoiceInitiated.Number);
            invoice.Status.ShouldBe(InvoiceStatus.Sent);

            invoice.IssuedTo.ShouldBe(invoiceInitiated.IssuedTo);
            invoice.InitiatedAt.ShouldBe(invoiceInitiated.InitiatedAt);

            invoice.IssuedBy.ShouldBe(invoiceIssued.IssuedBy);
            invoice.IssuedAt.ShouldBe(invoiceIssued.IssuedAt);

            invoice.SentVia.ShouldBe(invoiceSent.SentVia);
            invoice.SentAt.ShouldBe(invoiceSent.SentAt);
        }

        public aggregate_stream_samples(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}

