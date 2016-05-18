using System;
using System.Collections.Generic;
using System.ServiceModel.Syndication;
using System.Xml;
using DinnerParty.Models;
using Nancy;
using System.IO;

namespace DinnerParty.Modules
{
    public static class FormatterExtensions
    {
        public static Response AsRSS(this IResponseFormatter formatter, IEnumerable<Dinner> model, string RSSTitle)
        {
            return new RSSResponse(model, RSSTitle, formatter.Context.Request.Url);
        }
    }

    public class RSSResponse : Response
    {
        private string RSSTitle { get; set; }
        private Uri URL { get; set; }

        public RSSResponse(IEnumerable<Dinner> model, string RSSTitle, Uri URL)
        {
            this.RSSTitle = RSSTitle; ;
            this.URL = URL;

            Contents = GetXmlContents(model);
            ContentType = "application/rss+xml";
            StatusCode = HttpStatusCode.OK;
        }

        private Action<Stream> GetXmlContents(IEnumerable<Dinner> model)
        {
            var items = new List<SyndicationItem>();

            foreach (var d in model)
            {
                var contentString = String.Format("{0} with {1} on {2:MMM dd, yyyy} at {3}. Where: {4}, {5}",
                            d.Description, d.HostedBy, d.EventDate, d.EventDate.ToShortTimeString(), d.Address, d.Country);

                var item = new SyndicationItem(
                    title: d.Title,
                    content: contentString,
                    itemAlternateLink: new Uri("http://dinnerparty.azurewebsites.net/" + d.DinnerID),
                    id: "http://dinnerparty.azurewebsites.net/" + d.DinnerID,
                    lastUpdatedTime: d.EventDate.ToUniversalTime()
                    );
                item.PublishDate = d.EventDate.ToUniversalTime();
                item.Summary = new TextSyndicationContent(contentString, TextSyndicationContentKind.Plaintext);
                items.Add(item);
            }

            var feed = new SyndicationFeed(
                RSSTitle,
                RSSTitle, /* Using Title also as Description */
                URL,
                items);

            var formatter = new Rss20FeedFormatter(feed);

            return stream =>
            {
                using (var writer = XmlWriter.Create(stream))
                {
                    formatter.WriteTo(writer);

                }
            };
        }
    }
}
