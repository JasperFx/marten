using System;
using System.Collections.Generic;
using System.Linq;
using Nancy;
using Nancy.Responses.Negotiation;
using Nancy.Security;
using DinnerParty.Models;
using PagedList;
using Nancy.RouteHelpers;
using Nancy.ModelBinding;
using Nancy.Validation;
using Marten;

namespace DinnerParty.Modules
{
    public class DinnerModule : BaseModule
    {
        private readonly IDocumentSession _documentSession;
        private const int PAGE_SIZE = 25;

        public DinnerModule(IDocumentSession documentSession)
        {
            _documentSession = documentSession;
            const string basePath = "/dinners";

            Get[basePath] = Dinners;
            Get[basePath + "/page/{pagenumber}"] = Dinners;

            Get[basePath + "/{id}"] = parameters =>
            {
                if (!parameters.id.HasValue && String.IsNullOrWhiteSpace(parameters.id))
                {
                    return 404;
                }

                var dinner = documentSession.Load<Dinner>((int)parameters.id);

                if (dinner == null)
                {
                    return 404;
                }

                Page.Title = dinner.Title;
                Model.Dinner = dinner;

                return View["Dinners/Details", Model];
            };
        }

        private Negotiator Dinners(dynamic parameters)
        {
            Page.Title = "Upcoming Nerd Dinners";
            IQueryable<Dinner> dinners = null;

            //Searching?
            if (Request.Query.q.HasValue)
            {
                string query = Request.Query.q;

                dinners = _documentSession.Query<Dinner>().Where(d => d.Title.Contains(query)
                        || d.Description.Contains(query)
                        || d.HostedBy.Contains(query)).OrderBy(d => d.EventDate);
            }
            else
            {
                dinners = _documentSession.Query<Dinner>().Where(d => d.EventDate > DateTime.Now.Date).OrderBy(x => x.EventDate);
            }

            int pageIndex = parameters.pagenumber.HasValue && !String.IsNullOrWhiteSpace(parameters.pagenumber) ? parameters.pagenumber : 1;

            Model.Dinners = dinners.ToPagedList(pageIndex, PAGE_SIZE);

            return View["Dinners/Index", Model];
        }
    }

    public class DinnerModuleAuth : BaseModule
    {
        public DinnerModuleAuth(IDocumentSession documentSession)
            : base("/dinners")
        {
            this.RequiresAuthentication();

            Get["/create"] = parameters =>
            {
                var dinner = new Dinner()
                {
                    EventDate = DateTime.Now.AddDays(7)
                };

                Page.Title = "Host a Nerd Dinner";

                Model.Dinner = dinner;

                return View["Create", Model];
            };

            Post["/create"] = parameters =>
                {
                    var dinner = this.Bind<Dinner>();
                    var result = this.Validate(dinner);

                    if (result.IsValid)
                    {
                        var nerd = (UserIdentity)Context.CurrentUser;
                        dinner.HostedById = nerd.UserName;
                        dinner.HostedBy = nerd.FriendlyName;

                        var rsvp = new RSVP
                                    {
                                        AttendeeNameId = nerd.UserName,
                                        AttendeeName = nerd.FriendlyName
                                    };

                        dinner.RSVPs = new List<RSVP> { rsvp };

                        documentSession.Store(dinner);
                        documentSession.SaveChanges();

                        return Response.AsRedirect("/" + dinner.DinnerID);
                    }

                    Page.Title = "Host a Nerd Dinner";
                    Model.Dinner = dinner;
                    foreach (var item in result.Errors)
                    {
                        foreach (var error in item.Value)
                        {
                            Page.Errors.Add(new ErrorModel() { Name = item.Key, ErrorMessage = error.ErrorMessage });
                        }
                    }

                    return View["Create", Model];
                };

            Get["/delete/" + Route.AnyIntAtLeastOnce("id")] = parameters =>
                {
                    var dinner = documentSession.Load<Dinner>((int)parameters.id);

                    if (dinner == null)
                    {
                        Page.Title = "Nerd Dinner Not Found";
                        return View["NotFound", Model];
                    }

                    if (!dinner.IsHostedBy(Context.CurrentUser.UserName))
                    {
                        Page.Title = "You Don't Own This Dinner";
                        return View["InvalidOwner", Model];
                    }

                    Page.Title = "Delete Confirmation: " + dinner.Title;

                    Model.Dinner = dinner;

                    return View["Delete", Model];
                };

            Post["/delete/" + Route.AnyIntAtLeastOnce("id")] = parameters =>
                {
                    var dinner = documentSession.Load<Dinner>((int)parameters.id);

                    if (dinner == null)
                    {
                        Page.Title = "Nerd Dinner Not Found";
                        return View["NotFound", Model];
                    }

                    if (!dinner.IsHostedBy(Context.CurrentUser.UserName))
                    {
                        Page.Title = "You Don't Own This Dinner";
                        return View["InvalidOwner", Model];
                    }

                    documentSession.Delete(dinner);
                    documentSession.SaveChanges();

                    Page.Title = "Deleted";
                    return View["Deleted", Model];
                };

            Get["/edit" + Route.And() + Route.AnyIntAtLeastOnce("id")] = parameters =>
                {
                    var dinner = documentSession.Load<Dinner>((int)parameters.id);

                    if (dinner == null)
                    {
                        Page.Title = "Nerd Dinner Not Found";
                        return View["NotFound", Model];
                    }

                    if (!dinner.IsHostedBy(Context.CurrentUser.UserName))
                    {
                        Page.Title = "You Don't Own This Dinner";
                        return View["InvalidOwner", Model];
                    }

                    Page.Title = "Edit: " + dinner.Title;
                    Model.Dinner = dinner;

                    return View["Edit", Model];
                };

            Post["/edit" + Route.And() + Route.AnyIntAtLeastOnce("id")] = parameters =>
                {
                    var dinner = documentSession.Load<Dinner>((int)parameters.id);

                    if (!dinner.IsHostedBy(Context.CurrentUser.UserName))
                    {
                        Page.Title = "You Don't Own This Dinner";
                        return View["InvalidOwner", Model];
                    }

                    this.BindTo(dinner);

                    var result = this.Validate(dinner);

                    if (!result.IsValid)
                    {
                        Page.Title = "Edit: " + dinner.Title;
                        Model.Dinner = dinner;
                        foreach (var item in result.Errors)
                        {
                            foreach (var error in item.Value)
                            {
                                Page.Errors.Add(new ErrorModel() { Name = item.Key, ErrorMessage = error.ErrorMessage });
                            }
                        }

                        return View["Edit", Model];
                    }

                    documentSession.SaveChanges();

                    return Response.AsRedirect("/" + dinner.DinnerID);

                };

            Get["/my"] = parameters =>
                {
                    var nerdName = Context.CurrentUser.UserName;

                    var userDinners = documentSession.Query<Dinner>()
                                                     .Where(x => x.HostedById == nerdName || x.HostedBy == nerdName ||
                                                                 x.RSVPs.Any(r => r.AttendeeNameId == nerdName) ||
                                                                 x.RSVPs.Any(
                                                                     r =>
                                                                         r.AttendeeNameId == null &&
                                                                         r.AttendeeName == nerdName))
                                                     .OrderBy(x => x.EventDate)
                                                     .AsEnumerable();

                    Page.Title = "My Dinners";
                    Model.Dinners = userDinners;

                    return View["My", Model];
                };
        }
    }
}

