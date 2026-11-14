# Testing Results

Baseline:

```
┌───────────────────────────┬──────────────────────────┐
│ Test Name                 │ Duration in milliseconds │
├───────────────────────────┼──────────────────────────┤
│ SingleFileSimple          │                     5502 │
│ SingleFileFetchForWriting │                     5031 │
│ Multiples                 │                    27929 │
└───────────────────────────┴──────────────────────────┘

```

Adding Quick Append:

```
┌───────────────────────────┬──────────────────────────┐
│ Test Name                 │ Duration in milliseconds │
├───────────────────────────┼──────────────────────────┤
│ SingleFileSimple          │                     4517 │
│ SingleFileFetchForWriting │                     3338 │
│ Multiples                 │                    14722 │
└───────────────────────────┴──────────────────────────┘

```

Quick Append and the Inline + FetchForWriting thing:

```
┌───────────────────────────┬──────────────────────────┐
│ Test Name                 │ Duration in milliseconds │
├───────────────────────────┼──────────────────────────┤
│ SingleFileSimple          │                     4234 │
│ SingleFileFetchForWriting │                     3054 │
│ Multiples                 │                    15171 │
└───────────────────────────┴──────────────────────────┘

```

## `UseExclusiveLockOnConcurrentAppends` under OCC contention (#5764)

Rig: `dotnet run -c Release -- contention -c "<conn>" --rounds 4 --seconds 20 --writers 32 --streams N`
(`ContentionCommand.cs`). 32 writers loop `FetchForWriting` + append one event + `SaveChangesAsync`
against N hot streams, Quick append, one async single-stream projection running in the daemon.
A writer that loses with the unexpected-version exception counts a conflict and moves on (no retry).
Each scenario is 4 rounds per mode, interleaved, order flipped every round, same build, fresh
schema per run. Medians of 4. PostgreSQL 17.10 (docker, arm64), 18-core Mac, rig and DB on one
machine (shared with other work, so treat absolute numbers as indicative, relative as the signal).
"gaps" = `max(seq_id) - count(*)` on `mt_events`. "max lag" = worst observed unprocessed
events converted to ms at the achieved rate. "drain" = time for the daemon to go non-stale after writers stop.

| hot streams | mode | appends/s | p50 ms | p99 ms | seq gaps | max lag ms | drain ms |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 4 | default | 223 | 14.4 | 97.7 | 8407 | 19446 | 8326 |
| 4 | exclusive | 468 | 8.2 | 56.4 | 0 | 1292 | 506 |
| 32 | default | 1399 | 11.6 | 55.3 | 9851 | 9515 | 5030 |
| 32 | exclusive | 2518 | 6.7 | 44.6 | 0 | 1255 | 255 |
| 512 | default | 4366 | 5.9 | 28.1 | 2685 | 7740 | 5804 |
| 512 | exclusive | 5638 | 5.1 | 22.4 | 0 | 1199 | 1011 |

Every one of the 12 interleaved pairs favoured `exclusive` on throughput and on gaps (zero gaps in all
12 runs, against 2.5k-10k with the default). The survey's inference that a gap "only delays projections by the
stale-sequence threshold" holds, but the delay is large: the daemon trailed by 5-8 s after load stopped in every
default run. Caveats: losers do not retry here, so total useful work is what is compared; the
rig has no mix of non-OCC appends (unaffected by the option); a single-node database only.
