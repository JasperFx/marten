# DCB load test: Marten 10.0 (#5887 / PR #5892)

Measured 2026-11-13 on branch `fix/5887-dcb-conflict-check-10.0` (`718061b9d`) with this harness, in Release on .NET 10.0.14, on an 18-core Apple-silicon Mac. The database was a dedicated PostgreSQL 17.10 container built from the repo's image (`shm_size 512m`, `max_connections 500`, everything else default, including autovacuum, `shared_buffers=128MB` and `deadlock_timeout=1s`). Nothing else used that server, and every number here comes from a single TFM. Append mode is the default, `QuickWithServerTimestamps`. Reproduce any table with the command above it.

Docker on a laptop is no stand-in for production hardware, so read the **ratios** between configurations rather than the absolute rates.

## Recommendation

1. **Keep `TagValueAndEventType` as the 10.0 default.** On the #5887 reporter's workload it is the difference between 50/250 and 250/250 requests succeeding, at ~10x the commit rate, and its costs measured at noise level or below:
   - tagged-append throughput within ±8% of `TagValue`, while writing 3x the version rows;
   - the same HOT ratio, no more dead tuples per update, and the same steady-state decision throughput;
   - `mt_dcb_tag_version` 2-3x larger. That is the only real cost, and it is small: 400-600k rows, ~65 MB heap for 200k customers.
2. **Leave `DcbTagVersionFillFactor` opt-in.** Fillfactor 70 takes HOT from 98.4-99% to 100% and stops heap growth from updates, at the cost of a permanently larger heap: +42% at rest for `TagValue`, +43% for `TagValueAndEventType`. Without it, growth was ~10% over 1.4-4M updates and autovacuum held it there. The #5622 docs already say this; these numbers back them.
3. **No deadlocks anywhere.** None in any configuration, with Marten write retries on or off, including a crossed-boundary probe built to provoke them: 0 in `pg_stat_database.deadlocks`, 0 client `40P01`, 0 "deadlock detected" lines in the server log. The sorted single pass in #5892 holds, so the "retries hide deadlocks as 1s stalls" concern does not arise. Retries on and retries off give the same numbers within noise.
4. **Two pre-existing scalability problems are bigger than anything #5887 changed**, and both are worth fixing before 10.0 ships, because DCB users hit them at modest store sizes:
   - **HStore appends sequential-scan `mt_events` for every tagged event.** A DCB decision at 1M events takes **61-68 ms in HStore against 0.8 ms in TagTables**. Filed as #5896.
   - **TagTables queries spanning two tag types read the whole store.** That is 328 ms at 1M events against 7 ms for an index-friendly rewrite, and it is the canonical "student AND course" boundary shape. Filed as #5897.
   - Until the first is fixed, **HStore is the wrong default to recommend for write-heavy DCB**. Use TagTables, or turn on `EnableUniqueIndexOnEventId`; see section 1b.

## 1. Tagged-append throughput (`append`)

`dotnet run -c Release --project src/DcbLoadTest -- append --single-saves 3000 --batch-saves 600 --concurrent-saves 200`, 3 repeats, median shown.

"Version-row writes/save" is the measured `n_tup_ins + n_tup_upd` delta on `mt_dcb_tag_version`, after closing the pool so every backend has flushed its statistics.

| Variant | Storage | Scope | saves/s (min-max) | p50 ms | p99 ms | version-row writes/save | vs TagValue |
|---|---|---|---|---|---|---|---|
| 1 event, 1 tag, sequential | TagTables | TagValue | 2,094 (1,502-2,123) | 0.47 | 1.26 | 1.0 | |
| | TagTables | TagValueAndEventType | 1,982 (1,915-2,076) | 0.48 | 0.85 | 2.0 | -5% |
| | HStore | TagValue | 1,696 (1,246-1,950) | 0.55 | 2.35 | 1.0 | |
| | HStore | TagValueAndEventType | 1,818 (1,724-1,904) | 0.54 | 0.94 | 2.0 | +7% |
| 10 events (2 types), customer + region tags, sequential | TagTables | TagValue | 1,052 (1,047-1,103) | 0.85 | 1.72 | 2.0 | |
| | TagTables | TagValueAndEventType | 1,104 (1,077-1,110) | 0.83 | 1.66 | 6.0 | +5% |
| | HStore | TagValue | 402 (401-405) | 2.41 | 3.98 | 2.0 | |
| | HStore | TagValueAndEventType | 406 (401-406) | 2.43 | 3.51 | 6.0 | +1% |
| same, untagged (baseline) | TagTables | - | 1,613 | 0.59 | 1.19 | 0 | |
| | HStore | - | 1,593 | 0.59 | 1.07 | 0 | |
| same, 16 writers, region 1 of 10 (hot version rows) | TagTables | TagValue | 5,235 (5,140-5,295) | 2.56 | 8.58 | 2.0 | |
| | TagTables | TagValueAndEventType | 5,131 (4,879-5,134) | 2.68 | 8.90 | 6.0 | -2% |
| | HStore | TagValue | 927 (861-976) | 16.1 | 28.0 | 2.0 | |
| | HStore | TagValueAndEventType | 856 (845-929) | 16.8 | 30.2 | 6.0 | -8% |

- **The per-event-type versions cost 2x-3x the version-row writes and no measurable throughput.** Each distinct (tag, event type) pair adds one row write per save: 1 → 2 for one event with one tag, and 2 → 6 for two event types × two tags. Every scope delta sits inside the min-max spread of the repeats. That holds even with 16 writers contending on 10 hot region rows, where the extra rows are locked inside the same sorted pass.
- After one repeat of every variant (47,620 events), `mt_dcb_tag_version` held 7,326 rows / 1.5 MB under `TagValue` and 19,006 rows / 4.0 MB under `TagValueAndEventType`.
- **HStore is far slower than TagTables whenever events are tagged**, independent of the scope. Untagged, the two are identical. See 1b.

### 1b. Why HStore appends are slow (#5896)

Under Quick append, HStore sets each event's tags afterwards with `update mt_events set tags = ... where id = $1`. `mt_events` has no index on `id` unless `EnableUniqueIndexOnEventId` is on, so every tagged event is a sequential scan of the whole table, plus a non-HOT rewrite of the row: `tags` is GIN-indexed. After the run above:

| `mt_events` | n_tup_upd | HOT | dead tuples | table + indexes |
|---|---|---|---|---|
| TagTables | 0 | 0 | 0 | 13 MB |
| HStore | 41,420 | 0 | 32,000-41,420 | 26 MB |

The cost grows with the store. Seeding 1M events through sessions took minutes for TagTables; HStore had reached 588k after 30 minutes and was still slowing, so the HStore read store was seeded with `BulkInsertEventsAsync` instead. At 1M events an HStore decision takes 61-68 ms (section 2).

`append --modes HStore,TagTables --scopes TagValue`, run without and then with `--event-id-index` (which sets `EnableUniqueIndexOnEventId = true`), saves/s, median of 3:

| Variant | HStore, no id index | HStore, id index | TagTables, id index |
|---|---|---|---|
| 1 event, 1 tag, sequential | 1,905 | 2,070 | 2,065 |
| 10 events, 2 tags, sequential | 409 | **1,130** (2.8x) | 1,071 |
| same, 16 writers | 965 | **4,636** (4.8x) | 5,144 |

**The unique index on `id` is a working mitigation today.** The scan is the dominant cost; the non-HOT rewrite costs the remaining ~10% under concurrency. The fix in #5896 is to write the tags in the insert, which is what the bulk import already does.

## 2. `FetchForWritingByTags` latency (`read`)

`read --seed-streams 1000` / `--seed-streams 100000`: 10 events per customer, each also tagged with 1 of 1,000 regions, 1,000 sequential iterations, random customer. "Decision" means fetch + `AppendOne` + `SaveChangesAsync`, uncontended.

| Store | Storage | Scope | Query | events | fetch p50 | fetch p99 | decision p50 | decision p99 |
|---|---|---|---|---|---|---|---|---|
| 10k | TagTables | TagValue | 1 tag | ~10-16 | 0.35 | 0.47 | 0.97 | 1.29 |
| 10k | TagTables | TagValueAndEventType | 1 tag | ~10-19 | 0.26 | 0.36 | 0.80 | 1.08 |
| 10k | TagTables | either | 1 tag + AndEventsOfType | 5 | 0.23-0.28 | 0.33-0.38 | 0.79-0.86 | 1.13-1.73 |
| 10k | TagTables | either | no events yet | 0 | 0.20-0.21 | 0.26-0.31 | 0.70-0.72 | 0.87-0.92 |
| 10k | TagTables | either | customer OR region | ~25 | **6.4-7.5** | 7.7-9.0 | 7.5-8.8 | 10.1-10.5 |
| 10k | HStore | TagValue | 1 tag | 10 | 0.23 | 0.33 | 1.28 | 2.44 |
| 10k | HStore | either | customer OR region | ~25 | 1.0-1.6 | 1.9-2.0 | 2.2-2.7 | 2.9-3.6 |
| **1M** | TagTables | TagValue | 1 tag | 10 | 0.35 | 0.44 | 0.99 | 1.46 |
| **1M** | TagTables | TagValueAndEventType | 1 tag | 10 | 0.25 | 0.33 | 0.80 | 0.97 |
| **1M** | TagTables | either | 1 tag + AndEventsOfType | 5 | 0.23-0.31 | 0.28-0.55 | 0.81-0.91 | 1.30-1.47 |
| **1M** | TagTables | either | no events yet | 0 | 0.20-0.21 | 0.24-0.30 | 0.73-0.76 | 1.04-1.46 |
| **1M** | TagTables | either | customer OR region | 1,010 | **327-328** | 367-383 | 328-329 | 388-405 |
| **1M** | HStore | TagValue | 1 tag | 10 | 0.22 | 0.34 | **61.4** | 66.8 |
| **1M** | HStore | TagValue | 1 tag + AndEventsOfType | 5 | 0.35 | 0.40 | **63.0** | 74.5 |
| **1M** | HStore | TagValue | no events yet | 0 | 0.38 | 0.44 | **62.1** | 65.9 |
| **1M** | HStore | TagValue | customer OR region | 1,010 | 5.8 | 7.6 | 67.9 | 94.4 |

- **The scope does not change read latency.** The version capture is one primary-key lookup per distinct key either way. Where the two scopes differ above, the run order explains it, not the scope; see the note below.
- **Single-tag-type reads are flat from 10k to 1M events** on both storage modes: 0.2-0.35 ms for a fetch, under 1 ms for a TagTables decision.
- **TagTables, two tag types: cost scales with store size** (#5897). The `OR` sits above two `LEFT JOIN`s, so PostgreSQL hash-joins all of `mt_events` with both tag tables: three parallel sequential scans to return 1,014 rows. The same query written as `seq_id in (select … from tag_a where value = $1 union select … from tag_b where value = $2)` runs in 7.1 ms. `QueryByTagsAsync` builds the same SQL; `EventsExistAsync` does not.
- **HStore decisions at 1M events cost ~62 ms**, entirely the sequential scan from 1b. HStore fetches themselves are fast, using the GIN index.

**About the HStore scope differences.** HStore fetch latency rises as decisions accumulate, whatever the scope. Rerun with the scopes in reverse order, the second scope was again the slower one: `TagValue` 1.32 ms, `TagValueAndEventType` 1.26 ms, at 1M events. A single-tag fetch went 0.22 → 0.77 → 1.3 ms over three batches of a few thousand decisions. That is consistent with the per-event `UPDATE`s filling the GIN pending list and leaving dead tuples, and it is another reason to fix #5896.

## 3. Contended decisions (`contend`)

`contend` follows the #5887 reporter's shape: 50 requests released together on one class, 5 rounds (250 requests). Each request fetches the class boundary (`class AND (ClassScheduled, SpotBooked)`), appends one event and saves, retrying `DcbConcurrencyException` up to 2 more times with no wait, like Wolverine's `RetryTimes(2)`. The column `book` is the share of requests that append `SpotBooked`, which matches everyone's query, while seats remain (capacity 100); the rest append `WaitlistJoined`, which matches nobody's. "check" verifies the result is correct: booked ≤ capacity, and events appended = successful requests. A lost conflict would show as overbooking, a replayed commit as extra events.

| book | Storage | Scope | Write retries | succeeded | DcbConcurrencyException | 1st-attempt conflicts | commits/s | request p50 / p99 ms | save p50 / p99 ms | deadlocks | check |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | TagTables | TagValue | on | 42/250 | 681 | 97.2% | 115 | 56.5 / 103.9 | 1.65 / 22.2 | 0 | ok |
| 0 | TagTables | TagValue | off | 51/250 | 664 | 97.2% | 149 | 55.5 / 81.5 | 1.68 / 7.99 | 0 | ok |
| 0 | TagTables | TagValueAndEventType | on | **250/250** | **0** | 0% | **1,570** | 20.4 / 44.8 | 14.5 / 29.2 | 0 | ok |
| 0 | TagTables | TagValueAndEventType | off | **250/250** | **0** | 0% | **1,771** | 17.7 / 43.2 | 13.8 / 26.9 | 0 | ok |
| 0 | HStore | TagValue | on | 61/250 | 653 | 97.2% | 188 | 52.0 / 75.2 | 1.58 / 7.38 | 0 | ok |
| 0 | HStore | TagValue | off | 50/250 | 669 | 97.2% | 159 | 50.7 / 79.6 | 1.53 / 13.1 | 0 | ok |
| 0 | HStore | TagValueAndEventType | on | **250/250** | **0** | 0% | **1,793** | 17.1 / 41.9 | 13.1 / 31.1 | 0 | ok |
| 0 | HStore | TagValueAndEventType | off | **250/250** | **0** | 0% | **1,796** | 16.4 / 45.7 | 12.5 / 29.5 | 0 | ok |
| 0.2 | TagTables | TagValue | on/off | 52, 51 | 661, 669 | ~97% | 166 | ~49 / 68-74 | ~1.5 / 10-11 | 0 | ok |
| 0.2 | TagTables | TagValueAndEventType | on/off | 184, 158 | 403, 487 | 81-94% | 415-521 | 48-56 / 79-87 | 3.0-3.5 / 14-22 | 0 | ok |
| 0.2 | HStore | TagValue | on/off | 56, 54 | 661, 659 | ~97% | 157-182 | ~50 / 72-101 | ~1.5 / 13-16 | 0 | ok |
| 0.2 | HStore | TagValueAndEventType | on/off | 181, 175 | 419, 419 | 83-88% | 476-481 | ~51 / 83-88 | ~3 / 15-17 | 0 | ok |
| 1 | TagTables | TagValue | on/off | 51, 52 | 664, 655 | 96-98% | 160 | ~52 / 71-73 | ~1.6 / 10-15 | 0 | ok |
| 1 | TagTables | TagValueAndEventType | on/off | 78, 73 | 626, 625 | ~97% | 180-184 | ~61 / 89-97 | ~2.4 / 9-11 | 0 | ok |
| 1 | HStore | TagValue | on/off | 57, 52 | 651, 663 | 96-98% | 149-181 | 50-56 / 69-85 | ~1.7 / 9-13 | 0 | ok |
| 1 | HStore | TagValueAndEventType | on/off | 76, 83 | 634, 609 | ~97% | 180-206 | ~62 / 88-92 | ~2.2 / 7-8 | 0 | ok |

The deadlock column is the three counts summed: `pg_stat_database.deadlocks` delta, client `40P01`, and "deadlock detected" lines in the container log. All three were 0 in every row.

- **#5887 is fixed as reported.** The reporter saw 39/250 succeed with 211 failed; `TagValue` reproduces that at 42-61/250. Under `TagValueAndEventType`, waitlist joins no longer conflict with each other at all.
- **Correct conflicts still fire.** When everyone books (book = 1), both scopes conflict about 97% of the time on the first attempt, as they should, and the check never fails. `TagValueAndEventType` landed a few more requests here (73-83 vs 51-57). That is consistent across storage modes and retry settings, but it reflects timing (its saves take slightly longer, which staggers the retry waves), not semantics.
- **The serialization cost is real but bounded.** Non-conflicting decisions on one tag value still queue on that value's version rows: the check's no-op `DO UPDATE` takes the row lock. That is why the save p50 is ~13 ms when 50 commit back to back. On this machine it puts a ceiling of ~1,800 commits/s on a single hot tag value.
- **Marten write retries made no difference**, because there were no deadlocks to retry.

### 3b. Crossed boundaries (`crossed`)

`crossed` runs 50 concurrent decisions over 4 classes, 5 rounds. Each decision reads class X and appends to class Y ≠ X, so concurrent saves check X and bump Y while others check Y and bump X. Under TagTables, half the appends also carry X; HStore can hold only one value per tag type (#5265), so there every append carries Y alone.

| Storage | Scope | Write retries | succeeded | DcbConcurrencyException | commits/s | save p50 / p99 ms | deadlocks | check |
|---|---|---|---|---|---|---|---|---|
| TagTables | TagValue | on | 162/250 | 463 | 501 | 2.04 / 24.1 | 0 | ok |
| TagTables | TagValue | off | 163/250 | 465 | 500 | 2.01 / 13.9 | 0 | ok |
| TagTables | TagValueAndEventType | on | 215/250 | 339 | 674 | 2.80 / 29.4 | 0 | ok |
| TagTables | TagValueAndEventType | off | 205/250 | 362 | 714 | 2.52 / 20.2 | 0 | ok |
| HStore | TagValue | on | 219/250 | 283 | 939 | 3.00 / 35.9 | 0 | ok |
| HStore | TagValue | off | 193/250 | 378 | 738 | 2.02 / 29.9 | 0 | ok |
| HStore | TagValueAndEventType | on | 214/250 | 323 | 910 | 3.37 / 22.6 | 0 | ok |
| HStore | TagValueAndEventType | off | 220/250 | 276 | 981 | 4.64 / 34.6 | 0 | ok |

There were no deadlocks in this crossed shape either.

## 4. `mt_dcb_tag_version` growth and HOT ratio (`growth`)

`growth --modes TagTables` uses 200,000 customers, each pre-created with one `OrderPlaced`. Then 16 writers run for 300 s; each decision reads `customer AND OrderPlaced` for a uniformly random customer and appends `OrderShipped`. Autovacuum is at defaults. The version table is independent of the storage mode, so only TagTables was run.

| Scope | fillfactor | saves | steady saves/s | p50 / p99 ms | conflicts | version rows start → end | row updates/save | HOT | heap MB | index MB | autovacuums |
|---|---|---|---|---|---|---|---|---|---|---|---|
| TagValue | default | 1,640,531 | ~5,450 | 2.78 / 6.15 | 91 | 200k → 200k | 1.00 | 99.0% | 17.8 → 19.6 | 19.0 → 19.5 | 1 |
| TagValue | 70 | 1,362,646 | ~5,550 | 2.76 / 6.17 | 75 | 200k → 200k | 1.00 | **100%** | 25.2 → 25.2 | 18.9 → 18.9 | 1 |
| TagValueAndEventType | default | 1,397,246 | ~5,500 | 2.77 / 6.05 | 0 | 400k → 600k | 2.86 | 98.4% | 38.6 → 65.8 | 42.9 → 68.1 | 2 |
| TagValueAndEventType | 70 | 1,433,375 | ~5,550 | 2.76 / 5.91 | 0 | 400k → 600k | 2.86 | **100%** | 55.3 → 86.0 | 43.0 → 66.8 | 2 |

- **Each `TagValueAndEventType` decision writes 2.86 version rows against 1.00, and costs nothing in throughput or HOT ratio.** The writes are the check's no-op lock update on `customer:OrderPlaced`, plus the bumps of `customer` and `customer:OrderShipped`. Steady state was ~5,500 decisions/s in all four configurations.
- **Its growth is mostly new rows, not bloat.** The `(customer, OrderShipped)` rows appear as each customer ships for the first time (400k → 600k live rows). Allowing for those, the heap grew ~13% beyond its live data with the default fillfactor and ~3% at 70.
- **Fillfactor 70 gives 100% HOT and flat size**, but starts 42-43% larger. That matches the #5622 measurements, so it stays opt-in.
- **Conflicts are correct.** Under `TagValue`, two random decisions on the same customer conflict even though neither appends what the other reads (91 conflicts in 1.6M); under `TagValueAndEventType` none do. Same mechanism as section 3.
- **Something to watch, not a finding.** In three of the four runs, the first 30-60 s after pre-creating 200k rows ran at ~26 decisions/s. Recovery coincided with the first autovacuum/analyze, which points to plans made on stale statistics. Bulk-loading a DCB store and then running decisions immediately could see this; an `ANALYZE` after a large import is cheap insurance. The summary rows above use steady state.

## Issues filed

- JasperFx/marten#5896: HStore appends sequential-scan `mt_events` for every tagged event (no index on `id`); 62 ms decisions at 1M events. `EnableUniqueIndexOnEventId` mitigates it.
- JasperFx/marten#5897: TagTables queries over two tag types scan the whole store (`FetchForWritingByTags`, `QueryByTagsAsync`); 328 ms vs 7 ms rewritten at 1M events.
- JasperFx/marten#5898: `DcbStorageMode = HStore` discards a data source passed to `opts.Connection(NpgsqlDataSource)`, which then fails with "No password has been provided". The harness sets the storage mode first to work around it.
- JasperFx/jasperfx#1184: a `[BoundaryAggregate]` with an `Id` member gets a `<T, Guid>` evolver while Marten looks up `<T, string>`, so `FetchForWritingByTags` throws.

None of these is a regression from #5887 / #5892.

## Harness

`src/DcbLoadTest`: `append | read | contend | crossed | growth | compare` (the last is the original TagTables-vs-HStore comparison, unchanged). See `Program.cs` for the options. It needs its own database: statistics are read as deltas, and other sessions' load would show up in them. Two fixes to the existing harness:

- It now references `JasperFx.Events.SourceGenerator`. The `FetchForWritingByTags` benchmark could never run without it; the boundary aggregate had no dispatcher.
- The boundary aggregates carry `[BoundaryAggregate]`, and no `Id` member; see JasperFx/jasperfx#1184.
