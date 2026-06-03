using Xunit;

// #4617: structurally serialize this assembly's test runs. The
// `UseTenantPartitionedEvents` schema-creation path is currently exposed to a
// 42P07 / 23505 race when two cross-TFM test runs (net9 + net10) or two
// in-assembly classes race to CREATE the per-tenant partition with the same
// name. Three layers of defense are required for stability: (1) this attribute
// (in-assembly), (2) Environment.ProcessId in the fixture schema names
// (cross-TFM), (3) unique-per-test tenant ids (same-assembly siblings) — see
// the issue text for the full rationale.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
