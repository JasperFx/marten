namespace DinnerParty.Models
{
    public class RSVP
    {
        public int RsvpID { get; set; }
        public int DinnerID { get; set; }
        public string AttendeeName { get; set; }
        public string AttendeeNameId { get; set; }

        public virtual Dinner Dinner { get; set; }
    }
}