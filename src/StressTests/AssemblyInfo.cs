using Xunit;

// Disable xunit's default test-class-level parallelism for this assembly.
//
// StressTests is not parallel-safe by construction and never could be: its classes share one
// marten_testing database, `reset_all_data_usage_ihost` calls ResetAllData while sibling classes
// are asserting on unfiltered document counts, and `create_database_Tests` drops and re-creates
// databases underneath everything. Running its classes concurrently is not a stress test, it is a
// coin flip.
//
// Not caught until #5096 wired the project into CI, because nothing ran it. The symptom was six of
// its tests passing only on the supervisor's fresh-process retry — where each runs alone, which is
// what the class-level parallelism was denying them. Matches the convention in the thirteen other
// database-backed suites (CoreTests, LinqTests, DaemonTests, ...).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
