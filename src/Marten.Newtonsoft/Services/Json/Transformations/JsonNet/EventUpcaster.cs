#nullable enable
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Newtonsoft.Json.Linq;

namespace Marten.Services.Json.Transformations.JsonNet;

/// <summary>
///     <para>
///         Base implementation of <a href="https://www.newtonsoft.com/json">Json.Net</a>
///         <see cref="IEventUpcaster" /> transforming JSON payload from one CLR type to the other.
///         Upcasting is a process of transforming the old JSON schema into the new one.
///         You can use it to handle the event schema versioning/migration.
///         By deriving from it, you tell that for specific event type name, you'd like to get the new CLR event type.
///         The default event type name mapping will be used based on <c>TEvent</c> type, unless you override
///         <see cref="EventTypeName" /> property
///     </para>
///     <para>
///         You need to provide the implementation of <see cref="Upcast" /> method.
///         It should contain the logic transforming event payload from
///         <a href="https://www.newtonsoft.com/json/help/html/queryinglinqtojson.htm">Json.Net JObject</a> to
///         <c>TEvent</c>.
///         This logic will be both run in sync and async API.
///     </para>
///     <para>
///         Compared to the <see cref="Json.Transformations.EventUpcaster{TOldEvent,TEvent}" /> it allows to do more
///         performant processing.
///         <c>JObject</c>can help you to reduce the number of allocation and memory pressure.
///         By using this implementation, you also don't need to keep the old CLR type in the codebase. Yet, implementation
///         is a bit more tedious.
///     </para>
///     <para>
///         If you need to use async code in your transformation, derive from
///         <see cref="AsyncOnlyEventUpcaster{TEvent}" /> instead
///     </para>
///     <para>
///         Use
///         <c>
///             <see cref="Marten.Events.IEventStoreOptions.Upcast">store.options.Upcast</see>
///         </c>
///         method to register upcaster implementation
///         and tell Marten that you'd like to use it.
///     </para>
/// </summary>
/// <example>
///     Example implementation:
///     <code lang="csharp">
/// public class ShoppingCartOpenedUpcaster:
///     EventUpcaster&#60;ShoppingCartInitializedWithStatus&#62;
/// {
///     protected override ShoppingCartInitializedWithStatus Upcast(JObject oldEvent) =>
///         new ShoppingCartInitializedWithStatus(
///             (Guid)oldEvent["ShoppingCartId"]!,
///             new Client(
///                 (Guid)oldEvent["ClientId"]!
///             ),
///             ShoppingCartStatus.Opened
///         );
/// }
/// </code>
///     Example registration:
///     <code lang="csharp">
/// storeOptions.Events.Upcast&#60;ShoppingCartOpenedUpcaster&#62;();
/// </code>
/// </example>
/// <typeparam name="TEvent">Mapped CLR event type</typeparam>
public abstract class EventUpcaster<TEvent>: Transformations.EventUpcaster<TEvent>
    where TEvent : notnull
{
    public override object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index)
    {
        return JsonTransformations.FromDbDataReader(Upcast)(serializer, dbDataReader, index);
    }

    /// <summary>
    ///     <para>
    ///         Method defines the event JSON payload transformation using
    ///         <a href="https://www.newtonsoft.com/json">Json.Net</a>.
    ///         It "upcasts" one event schema into another.
    ///         You can use it to handle the event schema versioning/migration.
    ///     </para>
    ///     <para>
    ///         By defining it, you tell that instead of the old CLR type, for the specific event type name,
    ///         you'd like to get the new CLR event type.
    ///         Function takes the
    ///         <a href="https://www.newtonsoft.com/json/help/html/queryinglinqtojson.htm">Json.Net JObject</a>
    ///         and returns the new, mapped one of type <c>TEvent</c>.
    ///     </para>
    ///     <para>
    ///         In your application code, you should use only the new event type in the aggregation and projection logic.
    ///         See more in
    ///         <a href="https://martendb.io/events/versioning.htmll#raw-json-transformation-with-json-net-1">documentation</a>
    ///     </para>
    /// </summary>
    /// <example>
    ///     Example implementation:
    ///     <code lang="csharp">
    /// protected override ShoppingCartInitializedWithStatus Upcast(JObject oldEvent) =>
    ///     new ShoppingCartInitializedWithStatus(
    ///         (Guid)oldEvent["ShoppingCartId"]!,
    ///         new Client(
    ///             (Guid)oldEvent["ClientId"]!
    ///         ),
    ///         ShoppingCartStatus.Opened
    ///     );
    /// </code>
    /// </example>
    /// <param name="oldEvent">
    ///     JSON payload represented by
    ///     <a href="https://www.newtonsoft.com/json/help/html/queryinglinqtojson.htm">Json.Net JObject</a>,
    ///     to be transformed into <c>TEvent</c>
    /// </param>
    /// <returns>Instance of the <c>TEvent</c> transformed from <c>oldEvent</c>.</returns>
    protected abstract TEvent Upcast(JObject oldEvent);
}

/// <summary>
///     <para>
///         Base implementation of <a href="https://www.newtonsoft.com/json">Json.Net</a>
///         <see cref="IEventUpcaster" /> transforming asynchronously JSON payload from one CLR type to the other.
///         Upcasting is a process of transforming the old JSON schema into the new one.
///         You can use it to handle the event schema versioning/migration.
///         By deriving from it, you tell that for specific event type name, you'd like to get the new CLR event type.
///         The default event type name mapping will be used based on <c>TEvent</c> type, unless you override
///         <see cref="EventTypeName" /> property
///     </para>
///     <para>
///         You need to provide the implementation of <see cref="UpcastAsync" /> method.
///         It should contain the logic transforming event payload
///         <a href="https://www.newtonsoft.com/json/help/html/queryinglinqtojson.htm">Json.Net JObject</a> to
///         <c>TEvent</c>.
///     </para>
///     <para>
///         Compared to the <see cref="Json.Transformations.EventUpcaster{TOldEvent,TEvent}" /> it allows to do more
///         performant processing.
///         <c>JObject</c>can help you to reduce the number of allocation and memory pressure.
///         By using this implementation, you also don't need to keep the old CLR type in the codebase. Yet, implementation
///         is a bit more tedious.
///     </para>
///     <para>
///         <b>WARNING:</b> <c>UpcastAsync</c> method is called each type old event is read from database and deserialized.
///         <c>AsyncOnlyEventUpcaster</c> will only be run in the async API and throw exception when run in sync method
///         calls.
///         We discourage to run resource consuming methods here. It might end up with N+1 performance issue.
///         Best is to use sync transformation instead and deriving from <see cref="EventUpcaster{TEvent}" />
///     </para>
///     <para>
///         Use
///         <c>
///             <see cref="Marten.Events.IEventStoreOptions.Upcast">store.options.Upcast</see>
///         </c>
///         method to register upcaster implementation
///         and tell Marten that you'd like to use it.
///     </para>
/// </summary>
/// <example>
///     Example implementation:
///     <code lang="csharp">
/// public class ShoppingCartOpenedAsyncOnlyUpcaster:
///     AsyncOnlyEventUpcaster&#60;ShoppingCartInitializedWithStatus&#62;
/// {
///     private readonly IClientRepository _clientRepository;
/// 
///     public ShoppingCartOpenedAsyncOnlyUpcaster(IClientRepository clientRepository) =>
///         _clientRepository = clientRepository;
/// 
///     protected override async Task&#60;ShoppingCartInitializedWithStatus&#62; UpcastAsync(
///         JObject oldEvent,
///         CancellationToken ct
///     )
///     {
///         var clientId = (Guid)oldEvent["ClientId"]!;
///         var clientName = await _clientRepository.GetClientName(clientId, ct);
/// 
///         return new ShoppingCartInitializedWithStatus(
///             (Guid)oldEvent["ShoppingCartId"]!,
///             new Client(clientId, clientName),
///             ShoppingCartStatus.Opened
///         );
///     }
/// }
/// </code>
///     Example registration:
///     <code lang="csharp">
/// storeOptions.Events.Upcast&#60;ShoppingCartOpenedUpcaster&#62;();
/// </code>
/// </example>
/// <typeparam name="TEvent">Mapped CLR event type</typeparam>
/// <exception cref="MartenException">when upcaster is called in sync API</exception>
public abstract class AsyncOnlyEventUpcaster<TEvent>: Transformations.EventUpcaster<TEvent>
    where TEvent : notnull
{
    public override object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index)
    {
        throw new MartenException(
            $"Cannot use AsyncOnlyEventUpcaster of type {GetType().AssemblyQualifiedName} in the synchronous API.");
    }

    public override async ValueTask<object> FromDbDataReaderAsync(
        ISerializer serializer, DbDataReader dbDataReader, int index, CancellationToken ct
    )
    {
        return await JsonTransformations.FromDbDataReaderAsync(UpcastAsync)(serializer, dbDataReader, index, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     <para>
    ///         Method defines the async event JSON payload transformation using
    ///         <a href="https://www.newtonsoft.com/json">Json.Net</a>.
    ///         It "upcasts" one event schema into another. It "upcasts" one event schema into another.
    ///         You can use it to handle the event schema versioning/migration.
    ///     </para>
    ///     <para>
    ///         By defining it, you tell that instead of the old CLR type, for the specific event type name,
    ///         you'd like to get the new CLR event type.
    ///         Provided function takes the deserialized object of the old event type and returns the new, mapped one.
    ///     </para>
    ///     <para>
    ///         In your application code, you should use only the new event type in the aggregation and projection logic.
    ///         See more in
    ///         <a href="https://martendb.io/events/versioning.htmll#class-with-raw-json-transformation-with-json-net">documentation</a>
    ///     </para>
    ///     <para>
    ///         <b>WARNING!</b> <c>UpcastAsync</c> method is called each type old event is read from database and deserialized.
    ///         <c>AsyncOnlyEventUpcaster</c> will only be run in the async API and throw exception when run in sync method
    ///         calls.
    ///         We discourage to run resource consuming methods here. It might end up with N+1 performance issue.
    ///         Best is to use sync transformation instead and deriving from <see cref="EventUpcaster{TEvent}" />
    ///     </para>
    /// </summary>
    /// <example>
    ///     Example implementation:
    ///     <code lang="csharp">
    /// protected override async Task&#60;ShoppingCartInitializedWithStatus&#62; UpcastAsync(
    ///     JObject oldEvent,
    ///     CancellationToken ct
    /// )
    /// {
    ///     var clientId = (Guid)oldEvent["ClientId"]!;
    ///     var clientName = await _clientRepository.GetClientName(clientId, ct);
    /// 
    ///     return new ShoppingCartInitializedWithStatus(
    ///         (Guid)oldEvent["ShoppingCartId"]!,
    ///         new Client(clientId, clientName),
    ///         ShoppingCartStatus.Opened
    ///     );
    /// }
    /// </code>
    /// </example>
    /// <param name="oldEvent">
    ///     JSON payload represented by
    ///     <a href="https://www.newtonsoft.com/json/help/html/queryinglinqtojson.htm">Json.Net JObject</a>,
    ///     to be transformed into <c>TEvent</c>
    /// </param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Instance of the <c>TEvent</c> transformed from <c>oldEvent</c>.</returns>
    /// <exception cref="MartenException">when provided transformation is called in sync API</exception>
    protected abstract Task<TEvent> UpcastAsync(JObject oldEvent, CancellationToken ct);
}
