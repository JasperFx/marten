# Conclusion

In this tutorial, we started with a basic document-oriented approach to tracking freight shipments and gradually transformed it into a robust event-sourced system using Marten. Along the way, we highlighted why Marten’s unified approach is so powerful:

- **Unified Document & Event Store:** We saw how Marten allowed us to store JSON documents and event streams in the same PostgreSQL database, leveraging SQL reliability with NoSQL flexibility ([Projecting Marten Events to a Flat Table – The Shade Tree Developer](https://jeremydmiller.com/2022/07/25/projecting-marten-events-to-a-flat-table)).
- **ACID Transactions:** Marten gave us transactional consistency across both documents and events – updating an aggregate and its events together without losing consistency ([What would it take for you to adopt Marten? – The Shade Tree Developer](https://jeremydmiller.com/2021/01/11/what-would-it-take-for-you-to-adopt-marten/)).
- **Evolution to Event Sourcing:** We were able to introduce event sourcing incrementally. We began with a document model, then started recording events, and finally used projections to maintain the same document as a read model. This kind of gradual adoption is much harder if using separate technologies for state and events.
- **Projections and Queries:** Marten’s projections system let us derive new read models (like daily summaries) from the events with relative ease, all within our .NET code. We didn’t need external pipelines; the data stayed in PostgreSQL and remained strongly consistent thanks to Marten’s guarantees.
- **Integration with Tools:** By integrating with Wolverine, we glimpsed how to operate this system at scale, coordinating projections in a distributed environment and enabling modern deployment strategies like blue/green with minimal fuss ([Projection/Subscription Distribution | Wolverine](https://wolverinefx.net/guide/durability/marten/distribution.html)).

We also followed best practices such as clear event naming, encapsulating aggregate behavior in apply methods, using optimistic concurrency (via `FetchForWriting`), and separating the write and read concerns appropriately. These patterns will serve well in real-world applications.

Marten stands out in the .NET ecosystem by making advanced patterns (like CQRS and Event Sourcing) more accessible and pragmatic. It lets you start with a simple approach and incrementally add complexity (audit logging, temporal queries, analytics projections, etc.) as needed – all without switching databases or sacrificing transactional safety. This means you can adopt event sourcing in parts of your system that truly benefit from it (like shipments with complex workflows) while still handling simpler data as straightforward documents, all using one tool.

We encourage you to explore Marten’s documentation and experiment further:
- For a deeper dive into event sourcing, its core concepts, advanced implementation scenarios, and Marten's specific features, be sure to check out our comprehensive guide: [Understanding Event Sourcing with Marten](/events/learning).
- Try adding a new type of event (e.g., a `ShipmentDelayed` event) and see how to handle it in the projection.
- Implement a query that uses Marten’s `AggregateStreamAsync` LINQ integration for an ad-hoc calculation.
- If you have multiple bounded contexts, consider using separate schemas or databases with Marten, and possibly multiple DocumentStores.
- Look into Marten’s support for **sagas** (long-running workflows) and how events can drive them.

With Marten and PostgreSQL, you have a potent combination of conventional technology and innovative patterns. We hope this freight shipment example showed you how to harness Marten’s power in a real-world scenario. Happy coding with Marten!