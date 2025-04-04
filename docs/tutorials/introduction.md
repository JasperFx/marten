# Marten Tutorial: Building a Freight & Delivery System

> This tutorial introduces you to Marten through a real-world use case: building a freight and delivery management system using documents and event sourcing. You'll learn not just how to use Marten, but also why and when to apply different features, and how to integrate them with Wolverine for a complete CQRS and messaging architecture.

---

## What You Will Learn

- Why Marten's approach to Postgres as a document and event database is unique and powerful
- How to model real-world business workflows using documents and event sourcing
- How to define, store, and query domain models like shipments and drivers
- How to track the lifecycle of domain entities using event streams
- How to use projections to maintain real-time read models
- How to reliably send notifications using the Wolverine outbox
- How to scale with async projections and optimize performance

---

## Why Marten? The Power of Postgres + .NET

Many document databases and event stores are built on top of document-oriented NoSQL engines like MongoDB, DynamoDB, or Cosmos DB. Marten takes a different path: it builds a document store and event sourcing system **on top of PostgreSQL**, a relational database.

At first glance, this may seem unorthodox. Why use a relational database for document and event-based data?

The answer lies in the unique strengths of PostgreSQL:

- **ACID transactions** — PostgreSQL is battle-tested for transactional consistency. Marten builds on that to offer safe, predictable persistence of documents and events.
- **Powerful JSON support** — PostgreSQL's `jsonb` data type lets Marten store .NET objects as raw JSON with indexing and querying capabilities.
- **Relational flexibility** — If needed, you can combine document-style storage with traditional columns or relational data in the same schema.
- **One database for everything** — No need to manage separate infrastructure for documents, events, messages, and relational queries. Marten and Wolverine build on the same reliable engine.

Using Marten gives you the flexibility of NoSQL without leaving the safety and robustness of PostgreSQL. This hybrid approach enables high productivity, strong consistency, and powerful event-driven architectures in .NET applications.