#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Exceptions;

namespace Marten.Services.Json.Transformations
{
    /// <summary>
    /// <para>Interface <c>IEventUpcaster</c> defines the general event payload transformation API.
    /// Upcasting is a process of transforming the old JSON schema into the new one.
    /// You can use it to handle the event schema versioning/migration.
    /// By implementing it, you tell that for specific event type name, you'd like to get the new CLR event type.</para>
    /// <para>As Marten supports sync and async API, you must also provide the transformation methods.
    /// They provide the default serializer that you can use for JSON transformations as a parameter.
    /// It also provides <see cref="DbDataReader">DbDataReader</see> together with index in which JSON payload should be read.</para>
    /// <para>Use <c><see cref="Marten.Events.IEventStoreOptions.Upcast">store.options.Upcast</see></c> method to register upcaster implementation
    /// and tell Marten that you'd like to use it.</para>
    /// <para>We recommend to depend on the built-in <c>IEventUpcaster</c> implementations.
    /// Custom implementations should only happen if you need to do something highly specific to your use case.
    /// See more in <a href="https://martendb.io/events/versioning.html#event-type-name-migration">documentation</a></para>
    /// </summary>
    public interface IEventUpcaster
    {
        /// <summary>
        /// Event type name that you would like to transform
        /// </summary>
        string EventTypeName { get; }
        /// <summary>
        /// The new CLR event type you are mapping to
        /// </summary>
        Type EventType { get; }

        /// <summary>
        /// <para>Method defines the event JSON payload transformation. It "upcasts" one event schema into another.
        /// You can use it to handle the event schema versioning/migration.</para>
        /// <para><b>WARNING!</b> this method will only be called in <b>sync</b> API.
        /// Define <see cref="FromDbDataReaderAsync"/> for the async one</para>
        /// </summary>
        /// <param name="serializer">Default serializer that you can use for JSON transformations as a parameter</param>
        /// <param name="dbDataReader"><see cref="DbDataReader">DbDataReader</see> to get the JSON payload from</param>
        /// <param name="index">Column index in which JSON payload should be read</param>
        /// <returns>Deserialized and transformed object of the CLR type defined in <see cref="EventType"></see></returns>
        object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index);

        /// <summary>
        /// <para>Method defines the event JSON payload transformation. It "upcasts" one event schema into another.
        /// You can use it to handle the event schema versioning/migration.</para>
        /// <para><b>WARNING!</b> this method will only be called in <b>async</b> API.
        /// Define <see cref="FromDbDataReader"/> for the sync one</para>
        /// </summary>
        /// <param name="serializer">Default serializer that you can use for JSON transformations as a parameter</param>
        /// <param name="dbDataReader"><see cref="DbDataReader">DbDataReader</see> to get the JSON payload from</param>
        /// <param name="index">Column index in which JSON payload should be read</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Deserialized and transformed object of the CLR type defined in <see cref="EventType"></see></returns>
        ValueTask<object> FromDbDataReaderAsync(ISerializer serializer, DbDataReader dbDataReader, int index,
            CancellationToken ct);
    }

    /// <summary>
    /// <para>Base implementation of <see cref="IEventUpcaster">IEventUpcaster</see> transforming JSON payload.
    /// Upcasting is a process of transforming the old JSON schema into the new one.
    /// You can use it to handle the event schema versioning/migration.
    /// By deriving from it, you tell that for specific event type name, you'd like to get the new CLR event type.
    /// The default event type name mapping will be used, unless you override <see cref="EventTypeName"/> property</para>
    /// <para>As Marten supports sync and async API, you must also provide the transformation methods.
    /// They provide the default serializer that you can use for JSON transformations as a parameter.
    /// It also provides <see cref="DbDataReader">DbDataReader</see> together with index in which JSON payload should be read.</para>
    /// <para>You should at least implement sync <see cref="FromDbDataReader"/>.
    /// By default <see cref="FromDbDataReaderAsync"/> calls sync method internally. You can override the default behavior.</para>
    /// <para>Use <c><see cref="Marten.Events.IEventStoreOptions.Upcast">store.options.Upcast</see></c> method to register upcaster implementation
    /// and tell Marten that you'd like to use it.</para>
    /// <para>We recommend to depend on the built-in <c>EventUpcaster</c> implementations.
    /// Custom implementations should only happen if you need to do something highly specific to your use case.
    /// See more in <a href="https://martendb.io/events/versioning.html#event-type-name-migration">documentation</a></para>
    /// </summary>
    public abstract class EventUpcaster: IEventUpcaster
    {
        public abstract Type EventType { get; }

        /// <summary>
        /// Event type name that you would like to transform. By default it uses the default convention
        /// </summary>
        public virtual string EventTypeName => EventType.GetEventTypeName();

        public abstract object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index);

        public virtual ValueTask<object>
            FromDbDataReaderAsync(ISerializer serializer, DbDataReader dbDataReader, int index, CancellationToken ct) =>
            new(FromDbDataReader(serializer, dbDataReader, index));
    }

    /// <summary>
    /// <para>Base implementation of <see cref="EventUpcaster">EventUpcaster</see> transforming JSON payload into generic event type.
    /// Upcasting is a process of transforming the old JSON schema into the new one.
    /// You can use it to handle the event schema versioning/migration.
    /// By deriving from it, you tell that for specific event type name, you'd like to get the new CLR event type.
    /// The default event type name mapping will be used based on <c>TEvent</c> type, unless you override <see cref="EventTypeName"/> property</para>
    /// <para>As Marten supports sync and async API, you must also provide the transformation methods.
    /// They provide the default serializer that you can use for JSON transformations as a parameter.
    /// It also provides <see cref="DbDataReader">DbDataReader</see> together with index in which JSON payload should be read.</para>
    /// <para>You should at least implement sync <see cref="FromDbDataReader"/>.
    /// By default <see cref="FromDbDataReaderAsync"/> calls sync method internally. You can override the default behavior.</para>
    /// <para>Use <c><see cref="Marten.Events.IEventStoreOptions.Upcast">store.options.Upcast</see></c> method to register upcaster implementation
    /// and tell Marten that you'd like to use it.</para>
    /// <para>We recommend to depend on the built-in <c>EventUpcaster</c> implementations.
    /// Custom implementations should only happen if you need to do something highly specific to your use case.
    /// See more in <a href="https://martendb.io/events/versioning.html#event-type-name-migration">documentation</a></para>
    /// </summary>
    /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
    public abstract class EventUpcaster<TEvent>: EventUpcaster
    {
        public override Type EventType => typeof(TEvent);
    }

    /// <summary>
    /// <para>Base implementation of <see cref="IEventUpcaster"/> transforming JSON payload from one CLR type to the other.
    /// Upcasting is a process of transforming the old JSON schema into the new one.
    /// You can use it to handle the event schema versioning/migration.
    /// By deriving from it, you tell that for specific event type name, you'd like to get the new CLR event type.
    /// The default event type name mapping will be used based on <c>TOldEvent</c> type, unless you override <see cref="EventTypeName"/> property</para>
    /// <para>You need to provide the implementation of <see cref="Upcast" /> method.
    /// It should contain the logic transforming event payload from <c>TOldEvent"</c> to <c>TEvent"</c>.
    /// This logic will be both run in sync and async API.</para>
    /// <para>If you need to use async code in your transformation, derive from <see cref="AsyncOnlyEventUpcaster{TOldEvent, TEvent}"/> instead</para>
    /// <para>Use <c><see cref="Marten.Events.IEventStoreOptions.Upcast">store.options.Upcast</see></c> method to register upcaster implementation
    /// and tell Marten that you'd like to use it.</para>
    /// </summary>
    /// <example>
    /// Example implementation:
    /// <code>
    /// public class ShoppingCartOpenedUpcaster:
    ///      EventUpcaster&#60;ShoppingCartOpened, ShoppingCartInitializedWithStatus&#62;
    /// {
    ///      protected override ShoppingCartInitializedWithStatus Upcast(
    ///          ShoppingCartOpened oldEvent) =>
    ///          new ShoppingCartInitializedWithStatus(
    ///              oldEvent.ShoppingCartId,
    ///              new Client(oldEvent.ClientId),
    ///              ShoppingCartStatus.Opened
    ///          );
    /// }
    /// </code>
    /// </example>
    /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
    /// <typeparam name="TOldEvent">Old CLR event type</typeparam>
    public abstract class EventUpcaster<TOldEvent, TEvent>: EventUpcaster<TEvent>
        where TOldEvent : notnull where TEvent : notnull
    {
        public override string EventTypeName => (typeof(TOldEvent)).GetEventTypeName();

        public override object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index) =>
            JsonTransformations.FromDbDataReader<TOldEvent, TEvent>(Upcast)(serializer, dbDataReader, index);

        public override ValueTask<object> FromDbDataReaderAsync(ISerializer serializer, DbDataReader dbDataReader,
            int index, CancellationToken ct) =>
            JsonTransformations.FromDbDataReaderAsync<TOldEvent, TEvent>(Upcast)(
                serializer, dbDataReader, index, ct
            );

        protected abstract TEvent Upcast(TOldEvent oldEvent);
    }

    public abstract class AsyncOnlyEventUpcaster<TOldEvent, TEvent>: EventUpcaster<TEvent>
        where TOldEvent : notnull where TEvent : notnull
    {
        public override string EventTypeName => (typeof(TOldEvent)).GetEventTypeName();

        public override object FromDbDataReader(ISerializer serializer, DbDataReader dbDataReader, int index) =>
            throw new MartenException(
                $"Cannot use AsyncOnlyEventUpcaster of type {GetType().AssemblyQualifiedName} in the synchronous API.");

        public override ValueTask<object> FromDbDataReaderAsync(ISerializer serializer, DbDataReader dbDataReader,
            int index, CancellationToken ct) =>
            JsonTransformations.FromDbDataReaderAsync<TOldEvent, TEvent>(UpcastAsync)(
                serializer, dbDataReader, index, ct
            );

        protected abstract Task<TEvent> UpcastAsync(TOldEvent oldEvent, CancellationToken ct);
    }
}
