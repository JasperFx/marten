using Marten;
using Microsoft.AspNetCore.Mvc;

namespace OpenTelemetrySample.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public class BookingController: ControllerBase
    {
        private readonly ILogger<BookingController> _logger;
        private readonly IDocumentSession documentSession;

        public BookingController(ILogger<BookingController> logger, IDocumentSession documentSession)
        {
            _logger = logger;
            this.documentSession = documentSession;
        }

        [HttpPost]
        public void Create()
        {
            //With Correlation
            documentSession.CorrelationId = Guid.NewGuid().ToString();
            CreateStreams();
            //Without Correlation
            documentSession.CorrelationId = null;
            CreateStreams();
            documentSession.SaveChanges();
        }

        private void CreateStreams()
        {
            var booking = Guid.NewGuid();
            var payment = Guid.NewGuid();
            documentSession.Events.StartStream<Booking>(booking, new BookingCreated(booking));
            documentSession.Events.StartStream<Payment>(payment, new PaymentCreated(payment, booking));
            documentSession.SaveChanges();
        }
    }
}
