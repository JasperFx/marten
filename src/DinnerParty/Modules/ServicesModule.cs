using System;
using System.Linq;
using DinnerParty.Models;
using Marten;

namespace DinnerParty.Modules
{
    public class ServicesModule : BaseModule
    {
        public ServicesModule(IDocumentSession documentSession)
            : base("/services")
        {
            Get["/RSS"] = parameters =>
                {
                    var dinners = documentSession.Query<Dinner>().Where(d => d.EventDate > DateTime.Now.Date).OrderBy(x => x.EventDate).AsEnumerable();

                    if (dinners == null)
                    {
                        Page.Title = "Nerd Dinner Not Found";
                        return View["NotFound", Model];
                    }

                    return Response.AsRSS(dinners, "Upcoming Nerd Dinners");
                };
        }
    }
}