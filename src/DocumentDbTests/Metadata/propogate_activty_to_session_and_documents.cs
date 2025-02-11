using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;
using Xunit;
using Shouldly;
using Xunit.Abstractions;

namespace DocumentDbTests.Metadata
{
    public class propogate_activty_to_session_and_documents(ITestOutputHelper testOutputHelper)
        : OneOffConfigurationsContext
    {
        private readonly ActivitySource _source = new(nameof(propogate_activty_to_session_and_documents));

        [Fact]
        public async Task activity_source_correlation()
        {
            StoreOptions(opts =>
            {
                opts.Policies.ForAllDocuments(x =>
                {
                    x.Metadata.CausationId.Enabled = true;
                    x.Metadata.CorrelationId.Enabled = true;
                    x.Metadata.Headers.Enabled = true;
                });
                opts.Schema.For<MetadataTarget>().Metadata(m =>
                {
                    m.CausationId.MapTo(x => x.CausationId);
                    m.CorrelationId.MapTo(x => x.CorrelationId);
                    m.Headers.MapTo(x => x.Headers);
                });
                opts.Listeners.Add(new ActivitySourceCorrelation());
            });

            // needed to get an activity created below
            using var provider = Sdk.CreateTracerProviderBuilder()
                .AddSource("*")
                .Build();

            using var activity = _source.StartActivity(nameof(activity_source_correlation));
            activity.ShouldNotBeNull();
            var doc = new MetadataTarget();

            theSession.Store(doc);
            await theSession.SaveChangesAsync();

            var metadata = await theSession.MetadataForAsync(doc);

            metadata.CausationId.ShouldBe(activity.SpanId.ToHexString());
            metadata.CorrelationId.ShouldBe(activity.TraceId.ToHexString());
            metadata.Headers.ShouldNotBeEmpty();

            await using var session2 = theStore.QuerySession();
            var doc2 = await session2.LoadAsync<MetadataTarget>(doc.Id);
            doc2.CorrelationId.ShouldBe(theSession.CorrelationId);

            var activityContext = ActivityContext.Parse(doc2.Headers["traceparent"].ToString()!, null);
            activityContext.TraceId.ToHexString().ShouldBe(metadata.CorrelationId);
            activityContext.SpanId.ToHexString().ShouldBe(metadata.CausationId);
        }

        [Fact]
        public async Task activity_source_commitevents()
        {
            List<Activity> exportedItems = [];

            StoreOptions(opts =>
                opts.Listeners.Add(new ActivitySourceEvents())
            );
            using var provider = Sdk.CreateTracerProviderBuilder()
                .AddSource("*")
                .AddInMemoryExporter(exportedItems)
                .Build();

            var activity = _source.StartActivity(nameof(activity_source_commitevents));
            activity.ShouldNotBeNull();
            using (activity)
            {
                ArgumentNullException.ThrowIfNull(activity);
                var doc = new MetadataTarget();

                theSession.Store(doc);
                await theSession.SaveChangesAsync();
                exportedItems.ShouldNotBeEmpty();
            }

            exportedItems.ShouldContain(activity);
            List<string> expected = ["beforesavechanges", "aftercommit"];
            activity.Events.Select(x => x.Name).ToList().ShouldBeEquivalentTo(expected);

            var printTree = exportedItems.PrintTree();
            testOutputHelper.WriteLine(printTree);

            // I used: https://github.com/VerifyTests/Verify
            // but maybe a little top much to add just like that...
            // await Verify(printTree);
        }

    }

    public class ActivitySourceCorrelation: DocumentSessionListenerBase
    {
        private static readonly TextMapPropagator _propagator = new CompositeTextMapPropagator([new BaggagePropagator(), new TraceContextPropagator()]);
        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            BeforeSaveChanges(session);
            return Task.CompletedTask;
        }

        public override void BeforeSaveChanges(IDocumentSession session)
        {
            var activity = Activity.Current;
            if (activity is null) return;

            // https://github.com/oskardudycz/EventSourcing.NetCore/blob/cf425a4607981139f21a6dd05809801a94dc1940/Core.Marten/OpenTelemetry/OpenTelemetryExtensions.cs#L19
            session.CausationId = activity.SpanId.ToHexString();
            session.CorrelationId = activity.TraceId.ToHexString();
            _propagator.Inject(new PropagationContext(activity.Context, Baggage.Current), session,
                (documentSession, key, value) => documentSession.SetHeader(key, value));

        }


    }

    public class ActivitySourceEvents: DocumentSessionListenerBase
    {
        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            BeforeSaveChanges(session);
            return Task.CompletedTask;
        }

        public override void BeforeSaveChanges(IDocumentSession session)
        {
            var activity = Activity.Current;
            if (activity is null) return;

            ActivityTagsCollection activityTagsCollection = new ()
            {
                { nameof(session.PendingChanges.Inserts).ToLower(), session.PendingChanges.Inserts().Count() },
                { nameof(session.PendingChanges.Updates).ToLower(), session.PendingChanges.Updates().Count() },
                { nameof(session.PendingChanges.Deletions).ToLower(), session.PendingChanges.Deletions().Count() },
            };

            activity.AddEvent(new ActivityEvent("beforesavechanges", tags: activityTagsCollection));
        }

        public override Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        {
            AfterCommit(session, commit);
            return Task.CompletedTask;
        }

        public override void AfterCommit(IDocumentSession session, IChangeSet commit)
        {
            var activity = Activity.Current;
            if (activity is null) return;

            ActivityTagsCollection activityTagsCollection = new ()
            {
                { nameof(commit.Inserted).ToLower(), commit.Inserted.Count() },
                { nameof(commit.Updated).ToLower(), commit.Updated.Count() },
                { nameof(commit.Deleted).ToLower(), commit.Deleted.Count() },
            };

            activity.AddEvent(new ActivityEvent("aftercommit", tags: activityTagsCollection));
        }

    }

// Inspired by: https://github.com/dotnet/aspire-samples/pull/361
    internal static class OpenTelemetryTraceExtensions
    {
        private static readonly HashSet<string> necessaryKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            // LATER: prune this list
            "Name",
            //"Kind",
            "http.request.method",
            "http.response.status_code",
            "http.route",
            //"network.protocol.version",
            //"server.address",
            //"server.port",
            //"url.path",
            //"url.scheme",
            "db.user: postgres",
            "db.name: catalogdb",
            "rpc.grpc.status_code",
            "grpc.method",
            //"url.full",
            //"db.connection_string",
            "db.statement",
        };

        public static string PrintTree(this List<Activity> trace)
        {
            var rootSpan = trace.Where(span => span.ParentId == null);
            var sb = new StringBuilder();
            foreach (var r in rootSpan)
            {
                trace.PrintSpanTree(sb, r, "", true);
            }

            return sb.ToString();
        }

        private static void PrintSpanTree(this List<Activity> trace, StringBuilder sb, Activity span, string indent,
            bool last)
        {
            sb.Append(indent);
            if (last)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"║");
                sb.Append($"{indent}╚══");
                indent += "    ";
            }
            else
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"║");
                sb.Append($"{indent}╠══");
                indent += "║   ";
            }

            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{span.Kind.GetSpanEmoji()} {Enum.GetName(span.Kind.GetType(), span.Kind)} '{span.DisplayName.Split("_")[0]}'"); //other: { JsonSerializer.Serialize(span)}
            AppendHeader($" Source: {span.Source.Name}");

            var filteredTags = span.AllProperties();
            if (filteredTags.Any())
            {
                AppendHeader(" 🏷️ Tags");
                // Append each property in the dictionary
                foreach (var property in filteredTags)
                {
                    AppendProp(property.Key, property.Value);
                }

            }

            foreach (var e in span.Events)
            {
                AppendHeader($" ⚡️ {e.Name}");
                var allProperties = e.AllProperties(false);
                if (allProperties.Any())
                {
                    AppendHeader("  ️  🏷️ Event tags");
                }
                foreach (var property in allProperties)
                {
                    AppendProp("  ️ " + property.Key, property.Value);
                }
            }

            var children = trace.Where(s => s.ParentSpanId == span.SpanId).ToList();
            for (var i = 0; i < children.Count; i++)
            {
                trace.PrintSpanTree(sb, children[i], indent, i == children.Count - 1);
            }

            void AppendProp(string propertyKey, string? propertyValue)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║  ️ {propertyKey}: {Inline(propertyValue)}");
            }

            void AppendHeader(string text)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}║{text}");
            }
        }


        private static Dictionary<string, string?> AllProperties(this Activity activity, bool filter = true) =>
            ToProps(filter, activity.TagObjects);

        private static Dictionary<string, string?> AllProperties(this ActivityEvent activity, bool filter = true) =>
            ToProps(filter, activity.Tags);

        private static Dictionary<string, string?> ToProps(bool filter,
            IEnumerable<KeyValuePair<string, object?>> keyValuePairs)
        {
            var activityTagObjects = keyValuePairs
                .Select(x => new KeyValuePair<string, string?>(x.Key, x.Value?.ToString()))
                .Where(x => necessaryKeys.Contains(x.Key) || !filter);

            var allProperties = new Dictionary<string, string?>(activityTagObjects, StringComparer.OrdinalIgnoreCase);
            return allProperties;
        }


        private static string Inline(string? text)
        {
            if (text is null) return string.Empty;

            return text.Replace("\r\n", "  ", StringComparison.OrdinalIgnoreCase)
                .Replace("\n", "  ", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSpanEmoji(this ActivityKind kind)
        {
            return kind switch
            {
                ActivityKind.Internal => "🔧",
                ActivityKind.Server => "🌐",
                ActivityKind.Client => "📤",
                ActivityKind.Producer => "📦",
                ActivityKind.Consumer => "📥",
                _ => "❓"
            };
        }
    }
}
