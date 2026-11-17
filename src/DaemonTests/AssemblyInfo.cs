using System.Runtime.CompilerServices;
// xunit.v3 4.x spelling of CollectionBehavior(DisableTestParallelization = true), which is a compile
// error there. DaemonTests moved to 4.x with Bobcat 1.0 (JasperFx/bobcat#562).
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
[assembly: InternalsVisibleTo("DaemonTests.ManualOnly")]
