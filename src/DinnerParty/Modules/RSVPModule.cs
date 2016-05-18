using System;
using System.Linq;
using Nancy.Security;
using DinnerParty.Models;
using Marten;

namespace DinnerParty.Modules
{
    public class RSVPAuthorizedModule : BaseModule
    {
        public RSVPAuthorizedModule(IDocumentSession documentSession)
            : base("/RSVP")
        {
            this.RequiresAuthentication();

            Post["/Cancel/{id}"] = parameters =>
            {
                var dinner = documentSession.Load<Dinner>((int)parameters.id);

                var rsvp = dinner.RSVPs
                    .SingleOrDefault(r => Context.CurrentUser.UserName == (r.AttendeeNameId ?? r.AttendeeName));

                if (rsvp != null)
                {
                    dinner.RSVPs.Remove(rsvp);
                    documentSession.SaveChanges();

                }

                return "Sorry you can't make it!";
            };

            Post["/Register/{id}"] = parameters =>
            {
                var dinner = documentSession.Load<Dinner>((int)parameters.id);

                if (!dinner.IsUserRegistered(Context.CurrentUser.UserName))
                {

                    var rsvp = new RSVP();
                    rsvp.AttendeeNameId = Context.CurrentUser.UserName;
                    rsvp.AttendeeName = ((UserIdentity)Context.CurrentUser).FriendlyName;

                    dinner.RSVPs.Add(rsvp);

                    documentSession.SaveChanges(); 
                }

                return "Thanks - we'll see you there!";
            };
        }
    }
}