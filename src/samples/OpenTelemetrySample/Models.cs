namespace OpenTelemetrySample
{
    public class Booking
    {
        public Guid Id { get; set; }
        public DateTime? Date { get; set; }

        public Booking(BookingCreated @event)
        {
            Id = @event.Id;
        }
    }

    public class Payment
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public void Apply(PaymentCreated created)
        {
            (Id, BookingId) = created;
        }
    }

    public record BookingCreated(Guid Id);
    public record PaymentCreated(Guid Id, Guid bookingId);
}
