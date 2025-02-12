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
    [Collection("metadata")]
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

        [Fact]
        public void activity_w3c_ids_understanding()
        {
            /*
                Activity when Idformat = ActivityIdFormat.W3C represents a:
                   https://www.w3.org/TR/trace-context/#version-format
                   The following version-format definition is used for version 00.
                   version-format   = trace-id "-" parent-id "-" trace-flags
                   trace-id         = 32HEXDIGLC  ; 16 bytes array identifier. All zeroes forbidden
                   parent-id        = 16HEXDIGLC  ; 8 bytes array identifier. All zeroes forbidden
                   trace-flags      = 2HEXDIGLC   ; 8 bit flags. Currently, only one bit is used. See below for details

                Example:
                    Value = 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
                    base16(version) = 00
                    base16(trace-id) = 4bf92f3577b34da6a3ce929d0e0e4736
                    base16(parent-id) = 00f067aa0ba902b7
                    base16(trace-flags) = 01  // sampled


                https://www.w3.org/TR/trace-context/#trace-id
                *trace-id*
                w3c: This is the ID of the whole trace forest and is used to uniquely identify a distributed trace through a system
                dotnet:
                https://www.w3.org/TR/trace-context/#parent-id
                *parent-id*
                w3c: This is the ID of this request as known by the caller (in some tracing systems, this is known as the span-id, where a span is the execution of a client request)
                dotnet:

              */

            // If we don't have a tracer activities won't be created
            using var tracer = Sdk.CreateTracerProviderBuilder().AddSource("*").Build();
            var trace_id = "4bf92f3577b34da6a3ce929d0e0e4736";
            var parent_id = "00f067aa0ba902b7";
            var traceParent = $"00-{trace_id}-{parent_id}-01";
            var rootSpan = ActivityContext.Parse(traceParent, null);
            using var span = _source.StartActivity(kind: ActivityKind.Internal, parentContext: rootSpan);
            const string childOperationName = "ChildActivity";
            using var childSpan = _source.StartActivity(childOperationName);

            span.ShouldNotBeNull();
            span.Id.ShouldNotBeNull();

            span.IdFormat.ShouldBe(ActivityIdFormat.W3C);
            span.Id.ShouldBe($"00-{trace_id}-{span.SpanId}-01");
            span.OperationName.ShouldBe(nameof(activity_w3c_ids_understanding));
            span.ParentId.ShouldBe(traceParent);
            span.TraceId.ToHexString().ShouldBe(trace_id);
            span.ParentSpanId.ToHexString().ShouldBe(parent_id);
            span.RootId.ShouldBe(span.TraceId.ToHexString());

            var spanContext = span.Context;
            spanContext.TraceId.ToHexString().ShouldBe(trace_id);
            spanContext.TraceId.ShouldBe(span.TraceId);

            spanContext.SpanId.ToHexString().ShouldNotBe(parent_id);
            spanContext.SpanId.ShouldBe(span.SpanId);
            EchoActivity(span, nameof(span));

            testOutputHelper.WriteLine("");
            childSpan.ShouldNotBeNull();
            EchoActivity(childSpan, nameof(childSpan));

            var childSpanContext = childSpan.Context;
            childSpanContext.TraceId.ShouldBe(rootSpan.TraceId);
            childSpanContext.SpanId.ShouldNotBe(span.SpanId);
            childSpan.ParentSpanId.ShouldBe(span.SpanId);
        }

        private void EchoActivity(Activity activity, string activityName)
        {
            testOutputHelper.WriteLine(activityName);
            testOutputHelper.WriteLine($"    {nameof(Activity.OperationName)}={activity.OperationName}");
            testOutputHelper.WriteLine($"    {nameof(Activity.Id)}={activity.Id}");

            testOutputHelper.WriteLine("w3c: trace_id in various places...");
            testOutputHelper.WriteLine($"    {nameof(Activity.RootId)}={activity.RootId}");
            testOutputHelper.WriteLine($"    {nameof(Activity.TraceId)}={activity.TraceId}");
            testOutputHelper.WriteLine($"    {nameof(Activity.Context)+"."+nameof(ActivityContext.TraceId)}={activity.Context.TraceId}");

            testOutputHelper.WriteLine("w3c: parent_id aka span_id");
            testOutputHelper.WriteLine($"    {nameof(Activity.SpanId)}={activity.SpanId}");
            testOutputHelper.WriteLine($"    {nameof(Activity.Context)+"."+nameof(ActivityContext.SpanId)}:{activity.Context.SpanId}");

            testOutputHelper.WriteLine("parentId or parentSpanId");
            testOutputHelper.WriteLine($"    {nameof(Activity.ParentSpanId)}={activity.ParentSpanId}");
            testOutputHelper.WriteLine($"    {nameof(Activity.ParentId)}={activity.ParentId}");


        }

        [Fact]
        public void activitity_correlations()
        {
            using var tracer = Sdk.CreateTracerProviderBuilder().AddSource("*").Build();
            var trace_id = "4bf92f3577b34da6a3ce929d0e0e4736";
            var parent_id = "00f067aa0ba902b7";
            var traceParent = $"00-{trace_id}-{parent_id}-01";
            var rootSpan = ActivityContext.Parse(traceParent, null);
            using var span = _source.StartActivity(kind: ActivityKind.Internal, parentContext: rootSpan);
            const string childOperationName = "ChildActivity";
            using var childSpan = _source.StartActivity(childOperationName);

            rootSpan.TraceId.ToHexString().ShouldBe(trace_id);
            span.TraceId.ToHexString().ShouldBe(trace_id);
            childSpan.TraceId.ToHexString().ShouldBe(trace_id);
            childSpan.Id.ShouldNotBe(traceParent);
            childSpan.SpanId.ShouldNotBe(rootSpan.SpanId);
            childSpan.SpanId.ShouldNotBe(span.SpanId);

            childSpan.Id.ShouldContain(childSpan.Context.TraceId.ToHexString());
            childSpan.Id.ShouldContain(childSpan.Context.SpanId.ToHexString());
        }

        [Fact]
        public async Task propogate_w3c_traceparent_as_caussation_and_trace_id_as_correlation()
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

            var traceparent = activity.Id;
            var trace_id = activity.TraceId.ToHexString();
            traceparent.ShouldContain(trace_id);
            metadata.CausationId.ShouldBe(traceparent);
            metadata.CorrelationId.ShouldBe(trace_id);

            await using var session2 = theStore.QuerySession();
            var doc2 = await session2.LoadAsync<MetadataTarget>(doc.Id);
            doc2.CausationId.ShouldBe(traceparent);
            doc2.CorrelationId.ShouldBe(trace_id);

            ActivityContext.Parse(doc2.CausationId, null).TraceId.ToHexString().ShouldBe(trace_id);
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
