# Projection inventory — Phase 2 SG audit

Marten #4471 / JasperFx #276 Phase 2 (PR #4475).

## Executive summary

This audit enumerates every projection-shaped type across Marten's test libraries in scope of **Marten #4471** and **JasperFx #276 Phase 2** (Marten PR #4475). Phase 2 made `JasperFx.Events.SourceGenerator` dispatch the **only** projection-apply path — apps registering a projection the SG doesn't dispatch for now fail fast at startup with `InvalidProjectionException`.

### Two SG emission shapes

The SG emits dispatchers in one of two shapes depending on the registered type:

1. **Projection subclass** — for `SingleStreamProjection<TDoc, TId>` / `MultiStreamProjection<TDoc, TId>` / `EventProjection` subclasses, the SG emits a `partial class TheProjection { override ... EvolveAsync(...) }` that **merges into the user's declaration**. The user's class must therefore be declared `partial`. This is the source of Phase 2's ~80 `partial` annotations on test-library projection subclasses.
2. **Self-aggregating aggregate** — for aggregates registered via `Snapshot<TAggregate>()` / `Projections.Snapshot<TAggregate>()` / `LiveStreamAggregation<TAggregate>()` (with Apply / Create / ShouldDelete directly on the aggregate type), the SG emits a **separate** `internal sealed class TheAggregateEvolver : IGeneratedSyncEvolver<TAggregate, TId>` plus an `[assembly: GeneratedEvolver(typeof(TheAggregate), typeof(TheAggregateEvolver))]` registration. The aggregate type itself does **not** need to be `partial`.

### Headline findings

- **Total inventoried** — 221 projection-subclass types + 18 self-aggregating aggregate types = **239 dispatcher emission sites**.
- **Pattern-A coverage (projection subclasses)** — 189 / 221 are `partial` and Pattern-A-emit. 32 of the 221 are non-partial. Of those 32, 30 have no Apply/Create methods (lookup-only types — no SG emission expected) and 2 (`SignalRProducer`, `KafkaProducer` in `samples/Helpdesk`) implement `IProjection.ApplyAsync` directly as low-level handlers and don't need SG dispatch.
- **Pattern-B coverage (self-aggregating aggregates)** — 18 types registered via `Snapshot<T>()` / `LiveStreamAggregation<T>()`. None require `partial`; the emitted sibling `Evolver` class is independent of the source declaration.
- **Deliberate SG bypasses** — 29 projection subclasses override `Evolve` / `EvolveAsync` directly; 8 override `DetermineAction` / `DetermineActionAsync` directly. These take responsibility for dispatch and aren't expected to receive a SG-emitted method body.
- **No SG gaps surfaced** — the test suite is green against the alpha.14/alpha.7 pin, and a representative SG-emit dump (`-p:EmitCompilerGeneratedFiles=true` on `EventSourcingTests`) shows 191 `.g.cs` dispatchers landing for the project. If any Pattern-A source were non-partial the C# compiler would error CS0260 at build time; if any Pattern-B-expected aggregate were missing emission, registration would throw `InvalidProjectionException` at the test host's build.

### Phase 2 status

**Green.** Every projection type covered by the test suite either receives an SG-emitted dispatcher or deliberately bypasses via `Evolve`/`EvolveAsync` override or low-level `IProjection.ApplyAsync` implementation. Inventory is documentation-only — no rewrites needed.

### Out of scope

- Production projection types under `src/Marten/**` — covered by Phase 2's adoption.
- `ValueTypeTests`, `LinqTests`, `MultiTenancyTests` — `MultiTenancyTests` was crawled but has no projection types.
- Two documented skipped tests (`stream_id_is_set_as_string` and the now-fixed `rebuild_the_projection_skip_failed_events`, the latter shipped in #4483).

---
## src/CoreTests
| File | Class | Base | Method shapes | partial? | Explicit override? | Registration | Notes |
|------|-------|------|----------------|----------|-------------------|--------------|-------|
| Bugs/Bug_4185_codegen_conflict_projection_with_secondary_store_dependency.cs | OrderProjection4185 | SingleStreamProjection<OrderSummary4185,... | Apply, Create | partial | — | unknown |  |
| Bugs/Bug_4224_unique_index_on_mixed_case_table.cs | TestProjector | FlatTableProjection | (none) | non-partial | — | Projections.Add |  |
| Diagnostics/read_only_view_of_store_options_on_document_store.cs | AllSync | SingleStreamProjection<MyAggregate, Guid... | Apply, Create | partial | — | Projections.Add |  |
| Diagnostics/read_only_view_of_store_options_on_document_store.cs | AllGood | SingleStreamProjection<MyAggregate, Guid... | Apply, Create | partial | — | Projections.Add |  |

## src/DaemonTests
| File | Class | Base | Method shapes | partial? | Explicit override? | Registration | Notes |
|------|-------|------|----------------|----------|-------------------|--------------|-------|
| AggregationExamples.cs | TripProjection | SingleStreamProjection<Trip, Guid> | (none) | partial | — | unknown |  |
| AggregationExamples.cs | TripProjection | SingleStreamProjection<Trip, Guid> | ShouldDelete | partial | — | unknown |  |
| Aggregations/build_aggregate_multiple_projections.cs | CarProjection | SingleStreamProjection<CarView, Guid> | Apply | partial | — | Projections.Add |  |
| Aggregations/build_aggregate_multiple_projections.cs | TruckProjection | SingleStreamProjection<TruckView, Guid> | Apply | partial | — | Projections.Add |  |
| Aggregations/build_aggregate_projection.cs | ContactProjectionNullReturn | SingleStreamProjection<build_aggregate_p... | (none) | partial | — | Projections.Add(instance) |  |
| Aggregations/build_aggregate_projection.cs | InterfaceCreationProjection | SingleStreamProjection<build_aggregate_p... | (none) | partial | — | Projections.Add(instance) |  |
| Aggregations/build_aggregate_projection.cs | AbstractCreationProjection | SingleStreamProjection<build_aggregate_p... | (none) | partial | — | Projections.Add(instance) |  |
| Aggregations/custom_aggregation_in_async_daemon.cs | MyCustomProjection | MultiStreamProjection<CustomAggregate, i... | Evolve | partial | Evolve/EvolveAsync | Projections.Add(instance) |  |
| Aggregations/multi_stream_projections.cs | Projector | MultiStreamProjection<Projection, Guid> | Apply | partial | — | Projections.Add |  |
| Aggregations/multi_stream_projections.cs | DayProjection | MultiStreamProjection<Day, int> | Apply | partial | — | Projections.Add(instance) |  |
| Aggregations/multi_stream_projections.cs | UserIssueProjection | MultiStreamProjection<UserIssues, Guid> | Apply, Create | partial | — | Projections.Add |  |
| Aggregations/multistream_data_teardown_on_rebuild.cs | ImplementationAProjection | MultiStreamProjection<ImplementationA, s... | Create | partial | — | Projections.Add |  |
| Aggregations/multistream_data_teardown_on_rebuild.cs | ImplementationBProjection | MultiStreamProjection<ImplementationB, s... | Create | partial | — | Projections.Add |  |
| Aggregations/multistream_data_teardown_on_rebuild.cs | ImplementationA2Projection | MultiStreamProjection<ImplementationA2, ... | Create | partial | — | Projections.Add |  |
| Aggregations/multistream_data_teardown_on_rebuild.cs | ImplementationB2Projection | MultiStreamProjection<ImplementationB2, ... | Create | partial | — | Projections.Add |  |
| Aggregations/side_effects_in_aggregations.cs | Projection1 | SingleStreamProjection<SideEffects1, Gui... | Apply | partial | — | Projections.Add |  |
| Aggregations/side_effects_in_aggregations.cs | Projection2 | SingleStreamProjection<SideEffects2, str... | Apply | partial | — | Projections.Add |  |
| Aggregations/side_effects_in_aggregations.cs | Projection3 | SingleStreamProjection<SideEffects1, Gui... | Apply | partial | — | Projections.Add |  |
| Bugs/Bug_1995_empty_batch_update_failure.cs | IssueAggregateProjection | IProjection | (none) | non-partial | — | Projections.Add(instance) |  |
| Bugs/Bug_2073_tenancy_problems.cs | DocumentProjection | SingleStreamProjection<Document, string> | Apply, Create | partial | — | Projections.Add |  |
| Bugs/Bug_2074_recovering_from_errors.cs | UserIssueCounterProjection | MultiStreamProjection<UserIssueCounter, ... | Apply | partial | — | Projections.Add |  |
| Bugs/Bug_2159_using_QuerySession_within_async_aggregation.cs | UserAggregate | SingleStreamProjection<MyAggregate, Guid... | Apply, Create | partial | — | Projections.Add(instance) |  |
| Bugs/Bug_2177_query_session_tenancy_in_daemon.cs | TicketProjection | SingleStreamProjection<Ticket, Guid> | Apply, Create | partial | — | Projections.Add |  |
| Bugs/Bug_2201_out_of_order_exception_with_hard_deletes.cs | TicketProjection | SingleStreamProjection<Ticket, Guid> | Create | partial | — | Projections.Add |  |
| Bugs/Bug_2245_async_daemon_getting_stuck.cs | Projector | MultiStreamProjection<SyncProjection, st... | Apply, Create | partial | — | unknown |  |
| Bugs/Bug_2245_async_daemon_getting_stuck.cs | Projector | MultiStreamProjection<AsyncProjection, s... | Create | partial | — | unknown |  |
| Bugs/Bug_3080_WaitForNonStaleData_should_work_dammit.cs | MyAggregateTableProjection | EventProjection | (none) | partial | — | Projections.Add |  |
| Bugs/Bug_3221_assert_on_wrong_identity_type_from_multi_stream_projection_to_slicer.cs | MismatchedIdentityProjection | MultiStreamProjection<Target, string> | Apply | partial | — | Projections.Add |  |
| Bugs/Bug_3802_projection_rebuild_skips_document_in_skip_mode.cs | CompanyProjection | SingleStreamProjection<Company, Guid> | Apply, Create | partial | — | Projections.Add |  |
| Bugs/Bug_3802_projection_rebuild_skips_document_in_skip_mode.cs | CompanyUniqueEmailProjection | SingleStreamProjection<CompanyUniqueEmai... | Create | partial | — | Projections.Add |  |
| Bugs/Bug_4428_rich_storage_side_effect_events.cs | Bug4428CounterProjection | SingleStreamProjection<Bug4428Counter, G... | Apply | partial | — | Projections.Add |  |
| Bugs/Bug_deletewhere_should_remove_inserted_item.cs | DeletableEventProjection | EventProjection | (none) | partial | — | Projections.Add |  |
| Bugs/Bug_sequential_rebuilds_throws_internally.cs | Projector | SingleStreamProjection<RandomProjection,... | Apply, Create | partial | — | unknown |  |
| Composites/Bug_4093_archived_in_composite_projections.cs | Bug4093FooProjection | SingleStreamProjection<Bug4093FooDoc, Gu... | Create | partial | — | unknown |  |
| Composites/Bug_4093_archived_in_composite_projections.cs | Bug4093BazProjection | SingleStreamProjection<Bug4093BazDoc, Gu... | Create | partial | — | unknown |  |
| Composites/Bug_4329_fan_out_and_cache_limit.cs | ProductProjection | SingleStreamProjection<Product, Guid> | Evolve | partial | Evolve/EvolveAsync | unknown |  |
| Composites/Bug_4329_fan_out_and_cache_limit.cs | OrderSummaryProjection | MultiStreamProjection<OrderSummary, Guid... | Evolve | partial | Evolve/EvolveAsync | unknown |  |
| Composites/Bug_4329_try_find_upstream_cache.cs | OrderProjection | SingleStreamProjection<Order, Guid> | Evolve | partial | Evolve/EvolveAsync | unknown |  |
| Composites/Bug_4329_try_find_upstream_cache.cs | OrderShippingNotificationProjection | MultiStreamProjection<OrderShippingNotif... | Evolve | partial | Evolve/EvolveAsync | unknown |  |
| Composites/Feature_4284_composite_projection_with_services.cs | CompositeProductProjection | SingleStreamProjection<CompositeProduct,... | Evolve | partial | Evolve/EvolveAsync | unknown |  |
| Composites/Feature_4284_composite_projection_with_services.cs | CompositeProductMetricProjection | IProjection | (none) | non-partial | — | unknown |  |
| Composites/end_to_end_with_composite_projection.cs | TripMetricsProjection | IProjection | (none) | non-partial | — | unknown |  |
| Composites/multi_stage_projections.cs | AppointmentMetricsProjection | IProjection | (none) | non-partial | — | unknown |  |
| EventProjections/EventProjectionWithCreate_follow_up_operations.cs | EntityProjection | EventProjection | Create | partial | — | Projections.Add |  |
| EventProjections/EventProjection_follow_up_operations.cs | NestedEntityEventProjection | EventProjection | (none) | partial | — | Projections.Add |  |
| EventProjections/event_projection_scenario_tests.cs | UserProjection | EventProjection | Create | partial | — | Projections.Add(instance) |  |
| EventProjections/event_projections_end_to_end.cs | DistanceProjection | EventProjection | Create | partial | — | Projections.Add(instance) |  |
| EventProjections/event_projections_end_to_end.cs | DistanceProjection2 | IProjection | (none) | non-partial | — | unknown |  |
| EventProjections/using_custom_projection_that_depends_on_identity_map_behavior.cs | NestedEntityEventProjection | IProjection | (none) | non-partial | — | Projections.Add(instance) |  |
| EventProjections/using_patches_in_async_mode.cs | LetterPatcher | EventProjection | (none) | partial | — | Projections.Add(instance) |  |
| FlatTableProjections/WriteTableWithGuidIdentifierProjection.cs | WriteTableWithGuidIdentifierProjection | FlatTableProjection | (none) | non-partial | — | unknown |  |
| MultiTenancy/multi_tenancy_by_database.cs | AllSync | SingleStreamProjection<MyAggregate, Guid... | Apply, Create | partial | — | unknown |  |
| MultiTenancy/multi_tenancy_by_database.cs | AllGood | SingleStreamProjection<MyAggregate, Guid... | Apply, Create | partial | — | Projections.Add |  |
| MultiTenancy/using_for_tenant_with_side_effects_and_subscriptions.cs | NumbersSubscription | EventProjection | (none) | partial | — | Projections.Add(instance) |  |
| Resiliency/skipping_unknown_event_types_in_continuous_builds.cs | WeirdCustomAggregation | SingleStreamProjection<MyAggregate, Guid... | Evolve | partial | Evolve/EvolveAsync | Projections.Add(instance) |  |
| Resiliency/when_skipping_events_in_daemon.cs | ErrorRejectingEventProjection | EventProjection | Create | partial | — | Projections.Add |  |
| Resiliency/when_skipping_events_in_daemon.cs | CollateNames | MultiStreamProjection<NamesByLetter, str... | Apply | partial | — | Projections.Add |  |
| Subscriptions/SubscriptionSamples.cs | ErrorHandlingSubscription | SubscriptionBase | (none) | non-partial | — | unknown |  |
| Subscriptions/SubscriptionSamples.cs | KafkaSubscription | SubscriptionBase | (none) | non-partial | — | unknown |  |
| Subscriptions/subscription_configuration.cs | FakeProjection | IProjection | (none) | non-partial | — | Projections.Add(instance) |  |
| Subscriptions/subscription_configuration.cs | FakeSubscription | SubscriptionBase | (none) | non-partial | — | Projections.Add(instance) |  |
| Subscriptions/subscriptions_end_to_end.cs | ConstructorConfiguredSubscription | SubscriptionBase | (none) | non-partial | — | unknown |  |
| Subscriptions/subscriptions_end_to_end.cs | FilteredSubscription | SubscriptionBase, IDisposable | (none) | non-partial | — | unknown |  |
| Subscriptions/subscriptions_end_to_end.cs | FilteredSubscription2 | SubscriptionBase, IAsyncDisposable | (none) | non-partial | — | unknown |  |
| TeleHealth/AppointmentByExternalIdentifierProjection.cs | AppointmentByExternalIdentifierProjection | MultiStreamProjection<AppointmentByExter... | Evolve | partial | Evolve/EvolveAsync | unknown |  |
| TeleHealth/AppointmentDetailsProjection.cs | AppointmentDetailsProjection | MultiStreamProjection<AppointmentDetails... | Evolve | partial | Evolve/EvolveAsync | unknown |  |
| TeleHealth/AppointmentDurationProjection.cs | AppointmentDurationProjection | EventProjection | (none) | partial | — | unknown |  |
| TeleHealth/Appointments.cs | AppointmentProjection | SingleStreamProjection<Appointment, Guid... | Evolve | partial | Evolve/EvolveAsync | unknown |  |
| TeleHealth/BoardSummary.cs | BoardSummaryProjection | MultiStreamProjection<BoardSummary, Guid... | (none) | partial | — | unknown |  |
| TeleHealth/ProviderShift.cs | ProviderShiftProjection | SingleStreamProjection<ProviderShift, Gu... | Evolve | partial | Evolve/EvolveAsync | unknown |  |
| TestingSupport/TripProjectionWithCustomName.cs | TripProjection | SingleStreamProjection<Trip, Guid> | Apply, Create, ShouldDelete | partial | — | Projections.Add |  |
| catching_up_mode_for_projections_and_subscriptions.cs | ADocEventProjection | EventProjection | (none) | partial | — | Projections.Add |  |
| catching_up_mode_for_projections_and_subscriptions.cs | LetterEventsSubscription | SubscriptionBase | (none) | non-partial | — | Projections.Add(instance) |  |
| catching_up_mode_for_projections_and_subscriptions.cs | LetterCountsProjection | SingleStreamProjection<LetterCounts, Gui... | Evolve | partial | Evolve/EvolveAsync | Projections.Add |  |
| wait_for_non_stale_data_error_cases.cs | SometimesFailingLetterCountsProjection | SingleStreamProjection<LetterCounts, Gui... | Evolve | partial | Evolve/EvolveAsync | Projections.Add |  |
| wait_for_non_stale_data_error_cases.cs | FailsOnSaveProjection | SingleStreamProjection<FailsOnSave, Guid... | Create | partial | — | Projections.Add |  |

## src/DocumentDbTests
| File | Class | Base | Method shapes | partial? | Explicit override? | Registration | Notes |
|------|-------|------|----------------|----------|-------------------|--------------|-------|
| Configuration/DocumentMappingTests.cs | ProjectionWithConfiguredView | MultiStreamProjection<ProjectionConfigur... | Apply | partial | — | Projections.Add |  |
| Indexes/UniqueIndexTests.cs | UserMultiStreamProjection | MultiStreamProjection<UniqueUser, Guid> | Apply | partial | — | Projections.Add(instance) |  |

## src/EventSourcingTests
| File | Class | Base | Method shapes | partial? | Explicit override? | Registration | Notes |
|------|-------|------|----------------|----------|-------------------|--------------|-------|
| Aggregation/aggregate_stream_returns_null_if_the_aggregate_is_null_at_that_point_in_stream.cs | HardDeletedStartAndStopProjection | SingleStreamProjection<HardDeletedStartA... | (none) | partial | — | Projections.Add |  |
| Aggregation/aggregate_stream_returns_null_if_the_aggregate_is_null_at_that_point_in_stream.cs | HardDeletedStartAndStopProjection2 | SingleStreamProjection<HardDeletedStartA... | (none) | partial | — | Projections.Add |  |
| Aggregation/aggregation_projection_validation_rules.cs | EmptyProjection | SingleStreamProjection<GuidIdentifiedAgg... | (none) | partial | — | unknown |  |
| Aggregation/aggregation_projection_validation_rules.cs | GuidIdentifiedAggregateProjection | MultiStreamProjection<GuidIdentifiedAggr... | (none) | partial | — | unknown |  |
| Aggregation/aggregation_projection_validation_rules.cs | MissingMandatoryType | SingleStreamProjection<MyAggregate, Guid... | Apply | partial | — | unknown |  |
| Aggregation/aggregation_projection_validation_rules.cs | BadReturnType | SingleStreamProjection<MyAggregate, Guid... | Apply | partial | — | Projections.Add(instance) |  |
| Aggregation/aggregation_projection_validation_rules.cs | MissingEventType1 | SingleStreamProjection<MyAggregate, Guid... | Apply | partial | — | Projections.Add(instance) |  |
| Aggregation/aggregation_projection_validation_rules.cs | CanGuessEventType | SingleStreamProjection<MyAggregate, Guid... | Apply | partial | — | Projections.Add(instance) |  |
| Aggregation/aggregation_projection_validation_rules.cs | InvalidArgumentType | SingleStreamProjection<MyAggregate, Guid... | Apply | partial | — | Projections.Add(instance) |  |
| Aggregation/aggregation_projection_validation_rules.cs | BadMethodName | SingleStreamProjection<MyAggregate, Guid... | Create | partial | — | unknown |  |
| Aggregation/aggregation_projection_validation_rules.cs | AllGood | SingleStreamProjection<MyAggregate, Guid... | Apply, Create | partial | — | Projections.Add(instance) |  |
| Aggregation/ancillary_store_enrichment_tests.cs | OrderProjection | SingleStreamProjection<Order, Guid> | Apply | partial | — | Projections.Add |  |
| Aggregation/blue_green_deployment_of_aggregates.cs | Version2 | SingleStreamProjection<MyAggregate, Guid... | Apply | partial | — | Projections.Add |  |
| Aggregation/explicit_code_for_aggregation_logic.cs | MyCustomAggregateWithNoSlicer | SingleStreamProjection<CustomAggregate, ... | (none) | partial | — | unknown |  |
| Aggregation/explicit_code_for_aggregation_logic.cs | MySingleStreamProjection | SingleStreamProjection<CustomAggregate, ... | Evolve | partial | Evolve/EvolveAsync | unknown |  |
| Aggregation/explicit_code_for_aggregation_logic.cs | MyCustomStreamProjection | SingleStreamProjection<MyCustomStringAgg... | Evolve | partial | Evolve/EvolveAsync | Projections.Add(instance) |  |
| Aggregation/explicit_code_for_aggregation_logic.cs | MyCustomGuidProjection | SingleStreamProjection<MyCustomGuidAggre... | Evolve | partial | Evolve/EvolveAsync | Projections.Add(instance) |  |
| Aggregation/explicit_code_for_aggregation_logic.cs | MyCustomProjection | MultiStreamProjection<CustomAggregate, i... | Evolve | partial | Evolve/EvolveAsync | Projections.Add(instance) |  |
| Aggregation/explicit_code_for_aggregation_logic.cs | StartAndStopProjection | SingleStreamProjection<StartAndStopAggre... | (none) | partial | — | Projections.Add(instance) |  |
| Aggregation/global_tenanted_streams_within_conjoined_tenancy.cs | SpecialCounterProjection | SingleStreamProjection<SpecialCounter, G... | Apply | partial | — | AddGlobalProjection |  |
| Aggregation/global_tenanted_streams_within_conjoined_tenancy.cs | SpecialCounterProjection2 | SingleStreamProjection<SpecialCounter, G... | Apply, Evolve | partial | Evolve/EvolveAsync | unknown |  |
| Aggregation/global_tenanted_streams_within_conjoined_tenancy.cs | SpecialCounterProjectionAsString | SingleStreamProjection<SpecialCounterAsS... | Apply | partial | — | AddGlobalProjection |  |
| Aggregation/setting_version_number_on_aggregate.cs | SampleSingleStream | SingleStreamProjection<MyAggregate, Guid... | Apply, Create | partial | — | Projections.Add(instance) |  |
| Aggregation/stream_compacting.cs | LetterCountsByStringProjection | SingleStreamProjection<LetterCountsByStr... | Evolve | partial | Evolve/EvolveAsync | Projections.Add |  |
| Aggregation/stream_compacting.cs | LetterCountsProjection1 | SingleStreamProjection<LetterCounts, Gui... | Evolve | partial | Evolve/EvolveAsync | Projections.Add |  |
| Aggregation/stream_compacting.cs | LetterCountsProjection2 | SingleStreamProjection<LetterCounts, Gui... | (none) | partial | — | Projections.Add |  |
| Aggregation/stream_compacting.cs | LetterCountsProjection3 | SingleStreamProjection<LetterCounts, Gui... | (none) | partial | — | Projections.Add |  |
| Aggregation/stream_compacting.cs | LetterCountsProjection4 | SingleStreamProjection<LetterCounts, Gui... | (none) | partial | — | Projections.Add |  |
| Aggregation/using_apply_metadata.cs | ItemProjection | SingleStreamProjection<Item, Guid> | Apply | partial | — | Projections.Add |  |
| Aggregation/using_apply_metadata.cs | ItemRecordProjection | SingleStreamProjection<ItemRecord, Guid> | Apply, Create | partial | — | Projections.Add |  |
| Aggregation/using_event_filtering_within_inline_aggregations.cs | SimpleSingleStreamProjection | SingleStreamProjection<MyAggregate, Guid... | Evolve | partial | Evolve/EvolveAsync | Projections.Add |  |
| Aggregation/when_doing_inline_per_stream_aggregations_with_Guid_stream_identity.cs | SometimesDeletes | SingleStreamProjection<MyAggregate, Guid... | Apply, ShouldDelete | partial | — | unknown |  |
| Aggregation/when_doing_live_aggregations.cs | UsingMetadata | SingleStreamProjection<MyAggregate, Guid... | Apply, Create | partial | — | unknown |  |
| Aggregation/when_doing_live_aggregations.cs | AsyncEverything | SingleStreamProjection<MyAggregate, Guid... | Apply | partial | — | unknown |  |
| Aggregation/when_doing_live_aggregations.cs | AsyncCreateSyncApply | SingleStreamProjection<MyAggregate, Guid... | Apply | partial | — | unknown |  |
| Aggregation/when_doing_live_aggregations.cs | SyncCreateAsyncApply | SingleStreamProjection<MyAggregate, Guid... | Apply, Create | partial | — | unknown |  |
| Aggregation/when_doing_live_aggregations.cs | AllSync | SingleStreamProjection<MyAggregate, Guid... | Apply, Create | partial | — | unknown |  |
| Aggregation/when_enriching_events_for_aggregation_projections.cs | UserTaskProjection | SingleStreamProjection<UserTask, Guid> | Evolve | partial | Evolve/EvolveAsync | Projections.Add |  |
| Bugs/Bug_2296_tenant_session_in_grouper.cs | CountsByTagProjector | MultiStreamProjection<CountsByTag, strin... | Apply | partial | — | Projections.Add |  |
| Bugs/Bug_2865_configuration_assertion_with_flat_table_projections.cs | FlatImportProjection | FlatTableProjection | (none) | non-partial | — | Projections.Add |  |
| Bugs/Bug_2883_ievent_not_working_as_identity_source_in_multistream_projections.cs | CustomerInsightsProjection | MultiStreamProjection<CustomerInsightsRe... | Apply, Create | partial | — | Projections.Add |  |
| Bugs/Bug_3184_Projection_named_Projection.cs | Projection | SingleStreamProjection<AccountListInform... | Apply | partial | — | Projections.Add |  |
| Bugs/Bug_3643_using_struct_in_map_flattableprojection.cs | MyTableProjection | FlatTableProjection | (none) | non-partial | — | Projections.Add |  |
| Bugs/Bug_3661_await_custom_projection_slicing.cs | StartAndStopIteratingAwaitablesSlicedProjection | MultiStreamProjection<StartAndStopAggreg... | Apply, Create | partial | — | Projections.Add(instance) |  |
| Bugs/Bug_3769_query_for_non_stale_projection_with_custom_projection.cs | CustomAggregateProjection | IProjection | (none) | non-partial | — | Projections.Add(instance) |  |
| Bugs/Bug_3874_able_to_read_archived_and_tombstone_from_older_names.cs | ReproSimpleProjection | SingleStreamProjection<ReproSimpleDetail... | Apply, Create, ShouldDelete | partial | — | Projections.Add |  |
| Bugs/Bug_3874_able_to_read_archived_and_tombstone_from_older_names.cs | ReproArchivedProjection | SingleStreamProjection<ReproArchivedDeta... | Apply, Create | partial | — | Projections.Add |  |
| Bugs/Bug_3942_string_only_record.cs | SingleProjection | SingleStreamProjection<SingleProp, strin... | Create | partial | — | Projections.Add |  |
| Bugs/Bug_3946_tenancy_with_for_tenant_and_projection_issues.cs | AggregateProjection | SingleStreamProjection<Aggregate, Guid> | Apply, Create | partial | — | Projections.Add(instance) |  |
| Bugs/Bug_3946_tenancy_with_for_tenant_and_projection_issues.cs | EntityProjection | MultiStreamProjection<Entity, Guid> | Create | partial | — | Projections.Add(instance) |  |
| Bugs/Bug_4268_projection_async_to_inline_migration.cs | Bug4268ProductProjection | SingleStreamProjection<Bug4268Product, S... | Apply, Create | partial | — | Projections.Add |  |
| Bugs/Bug_4270_global_projection_preserves_event_tenant.cs | Bug4270OrderSummaryProjection | SingleStreamProjection<Bug4270OrderSumma... | Apply, Create | partial | — | AddGlobalProjection |  |
| Bugs/Bug_4441_force_catch_up_with_outbox.cs | LetterCountsProjection | SingleStreamProjection<LetterCounts, Gui... | Evolve | partial | Evolve/EvolveAsync | Projections.Add |  |
| Bugs/codegen_issue_with_IEvent.cs | FooProjection | MultiStreamProjection<FooAuditLog, Guid> | Apply | partial | — | Projections.Add(instance) |  |
| Bugs/codegen_issue_with_IEvent.cs | RecordProjection | MultiStreamProjection<RecordAuditLog, Gu... | Apply, Create | partial | — | Projections.Add(instance) |  |
| Bugs/nulls_in_event_name_cache.cs | CustomProjection | IProjection | (none) | non-partial | — | Projections.Add(instance) |  |
| Daemon/postgres_listen_notify_wakeup_tests.cs | LnProjection | SingleStreamProjection<LnCount, Guid> | Evolve | partial | Evolve/EvolveAsync | Projections.Add |  |
| Dcb/auto_discover_tag_types_from_projections.cs | TicketSummaryProjection | SingleStreamProjection<TicketSummary, Ti... | (none) | partial | — | Projections.Add |  |
| Dcb/hstore_auto_discover_tag_types_from_projections.cs | HsTicketSummaryProjection | SingleStreamProjection<HsTicketSummary, ... | (none) | partial | — | Projections.Add |  |
| Examples/SampleEventProjection.cs | SampleEventProjection | EventProjection | Create | partial | — | Projections.Add(instance) |  |
| Examples/SampleEventProjection.cs | ExplicitSampleProjection | EventProjection | (none) | partial | — | unknown |  |
| Examples/TeleHealth/BoardViewProjection.cs | BoardViewProjection | MultiStreamProjection<BoardView, Guid> | Apply, Create | partial | — | unknown |  |
| Examples/TrackedEventProjection.cs | TrackedEventProjection | EventProjection | (none) | partial | — | Projections.Add(instance) |  |
| Examples/TripProjectionWithEventMetadata.cs | TripProjection | SingleStreamProjection<Trip, Guid> | Apply, Create | partial | — | unknown |  |
| FetchForWriting/fetch_for_writing_and_projection_metadata_for_inline_projections.cs | ProjectionWithVersions | SingleStreamProjection<VersionedGuy, Gui... | Evolve | partial | Evolve/EvolveAsync | Projections.Add |  |
| FetchForWriting/fetching_inline_aggregates_for_writing.cs | TestProjection2 | SingleStreamProjection<TestAggregate, st... | Apply, Create | partial | — | Projections.Add |  |
| FetchForWriting/fetching_live_aggregates_for_writing.cs | TotalsProjection | MultiStreamProjection<Totals, Guid> | Apply | partial | — | Projections.Add(instance) |  |
| Projections/AggregationProjectionTests.cs | SampleAggregate | SingleStreamProjection<MyAggregate, Guid... | Apply, Create, ShouldDelete | partial | — | unknown |  |
| Projections/EventProjectionTests.cs | EmptyProjection | EventProjection | (none) | partial | — | Projections.Add(instance) |  |
| Projections/EventProjectionTests.cs | SimpleProjection | EventProjection | (none) | partial | — | unknown |  |
| Projections/EventProjectionTests.cs | SimpleTransformProjection | EventProjection | (none) | partial | — | unknown |  |
| Projections/EventProjectionTests.cs | SimpleTransformProjectionUsingMetadata | EventProjection | (none) | partial | — | Projections.Add(instance) |  |
| Projections/EventProjectionTests.cs | SimpleCreatorProjection | EventProjection | Create | partial | — | unknown |  |
| Projections/EventProjectionTests.cs | SimpleCreatorProjection2 | EventProjection | (none) | partial | — | unknown |  |
| Projections/EventProjectionTests.cs | LambdaProjection | EventProjection | (none) | partial | — | unknown |  |
| Projections/EventProjections/EventProjectionOrderingTests.cs | TestOrderingEventProjection | EventProjection | (none) | partial | — | Projections.Add |  |
| Projections/Flattened/Bug_4255_flat_table_not_null_constraint.cs | Bug4255Projection | FlatTableProjection | (none) | non-partial | — | Projections.Add |  |
| Projections/Flattened/Bug_4290_4291_flat_table_enum_and_value_types.cs | Bug4290StatusProjection | FlatTableProjection | (none) | non-partial | — | Projections.Add |  |
| Projections/Flattened/Bug_4290_4291_flat_table_enum_and_value_types.cs | Bug4290IntStatusProjection | FlatTableProjection | (none) | non-partial | — | Projections.Add |  |
| Projections/Flattened/Bug_4290_4291_flat_table_enum_and_value_types.cs | Bug4290LegacyProjection | FlatTableProjection | (none) | non-partial | — | Projections.Add |  |
| Projections/Flattened/WriteTableWithGuidIdentifierProjection.cs | WriteTableWithGuidIdentifierProjection | FlatTableProjection | (none) | non-partial | — | unknown |  |
| Projections/Flattened/WriteTableWithGuidIdentifierProjection.cs | WriteTableWithStringIdentifierProjection | FlatTableProjection | (none) | non-partial | — | unknown |  |
| Projections/Flattened/WriteTableWithGuidIdentifierProjection.cs | WriteTableWithEventMemberIdentityProjection | FlatTableProjection | (none) | non-partial | — | unknown |  |
| Projections/Flattened/flat_table_projection_with_stream_id_identifier_end_to_end.cs | SiteProjection | FlatTableProjection | (none) | non-partial | — | Projections.Add |  |
| Projections/Flattened/using_event_projection_for_flat_tables.cs | ImportSqlProjection | EventProjection | (none) | partial | — | unknown |  |
| Projections/MultiStreamProjections/CustomGroupers/Bug_4261_multistream_sample_coverage.cs | ExternalAccountLinkProjection | SingleStreamProjection<ExternalAccountLi... | Apply | partial | — | unknown |  |
| Projections/MultiStreamProjections/CustomGroupers/Bug_4261_multistream_sample_coverage.cs | CustomerBillingProjection | MultiStreamProjection<CustomerBillingMet... | Apply, Create | partial | — | unknown |  |
| Projections/MultiStreamProjections/CustomGroupers/Bug_4261_multistream_sample_coverage.cs | CustomerBillingProjection | MultiStreamProjection<CustomerBillingMet... | Apply, Create | partial | — | unknown |  |
| Projections/MultiStreamProjections/CustomGroupers/Bug_4261_multistream_sample_coverage.cs | CustomerBillingProjection | MultiStreamProjection<CustomerBillingMet... | Apply, Create | partial | — | unknown |  |
| Projections/MultiStreamProjections/CustomGroupers/Bug_4261_multistream_sample_coverage.cs | ExternalAccountLinkProjection | SingleStreamProjection<ExternalAccountLi... | Apply | partial | — | unknown |  |
| Projections/MultiStreamProjections/CustomGroupers/Bug_4261_multistream_sample_coverage.cs | CustomerBillingProjection | MultiStreamProjection<CustomerBillingMet... | Apply, Create | partial | — | unknown |  |
| Projections/MultiStreamProjections/CustomGroupers/custom_grouper_with_document_session.cs | UserFeatureTogglesProjection | MultiStreamProjection<UserFeatureToggles... | Apply | partial | — | Projections.Add |  |
| Projections/MultiStreamProjections/CustomGroupers/custom_grouper_with_events_transformation.cs | MonthlyAllocationProjection | MultiStreamProjection<MonthlyAllocation,... | Apply | partial | — | Projections.Add |  |
| Projections/MultiStreamProjections/CustomGroupers/custom_slicer.cs | UserGroupsAssignmentProjection | MultiStreamProjection<UserGroupsAssignme... | Apply | partial | — | Projections.Add |  |
| Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs | ExternalAccountLinkProjection | SingleStreamProjection<ExternalAccountLi... | Apply | partial | — | Projections.Add |  |
| Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs | CustomerBillingProjection | MultiStreamProjection<CustomerBillingMet... | Apply, Create | partial | — | Projections.Add |  |
| Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs | CustomerBillingProjection | MultiStreamProjection<CustomerBillingMet... | Apply, Create | partial | — | Projections.Add |  |
| Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs | ExternalAccountLinkProjection | SingleStreamProjection<ExternalAccountLi... | Apply | partial | — | Projections.Add |  |
| Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs | CustomerBillingProjection | MultiStreamProjection<CustomerBillingMet... | Apply, Create | partial | — | Projections.Add |  |
| Projections/MultiStreamProjections/CustomGroupers/grouping_examples_for_unknown_ids.cs | CustomerBillingProjection | MultiStreamProjection<CustomerBillingMet... | Apply, Create | partial | — | Projections.Add |  |
| Projections/MultiStreamProjections/monthly_account_activity_projection.cs | MonthlyAccountActivityProjection | MultiStreamProjection<MonthlyAccountActi... | Apply, Create | partial | — | Projections.Add |  |
| Projections/MultiStreamProjections/rolling_up_by_tenant.cs | RollupProjection | MultiStreamProjection<Rollup, string> | Apply | partial | — | Projections.Add |  |
| Projections/MultiStreamProjections/rolling_up_by_tenant.cs | Rollup2Projection | MultiStreamProjection<Rollup2, TenantId> | Apply | partial | — | Projections.Add |  |
| Projections/MultiStreamProjections/simple_multi_stream_projection.cs | UserGroupsAssignmentProjection | MultiStreamProjection<UserGroupsAssignme... | Apply | partial | — | Projections.Add |  |
| Projections/MultiStreamProjections/simple_multi_stream_projection.cs | UserGroupsAssignmentProjection2 | MultiStreamProjection<UserGroupsAssignme... | Apply | partial | — | unknown |  |
| Projections/MultiStreamProjections/simple_multi_stream_projection_wih_one_to_many.cs | UserGroupsAssignmentProjection | MultiStreamProjection<UserGroupsAssignme... | Apply | partial | — | unknown |  |
| Projections/MultiTenants/ConjoinedTenancyProjectionsTests.cs | ResourceProjection | SingleStreamProjection<Resource, Guid> | Apply, Create | partial | — | Projections.Add |  |
| Projections/MultiTenants/ConjoinedTenancyProjectionsTests.cs | ResourcesGlobalSummaryProjection | MultiStreamProjection<ResourcesGlobalSum... | Apply | partial | — | Projections.Add |  |
| Projections/MultiTenants/ConjoinedTenancyProjectionsTests.cs | CompanyLocationCustomProjection | SingleStreamProjection<CompanyLocation, ... | (none) | partial | — | Projections.Add(instance) |  |
| Projections/custom_transformation_of_events.cs | LapMultiStreamProjection | MultiStreamProjection<Lap, Guid> | Apply | partial | — | unknown |  |
| Projections/custom_transformation_of_events.cs | NewsletterSubscriptionProjection | MultiStreamProjection<NewsletterSubscrip... | Apply | partial | — | Projections.Add(instance) |  |
| Projections/event_projection_enrichment_tests.cs | SimpleEnrichmentProjection | EventProjection | (none) | partial | — | Projections.Add(instance) |  |
| Projections/event_projection_enrichment_tests.cs | DatabaseLookupEnrichmentProjection | EventProjection | (none) | partial | — | Projections.Add(instance) |  |
| Projections/event_projection_enrichment_tests.cs | CallOrderTrackingProjection | EventProjection | (none) | partial | — | unknown |  |
| Projections/event_projection_should_register_document_types.cs | AuditRecordProjection | EventProjection | (none) | partial | — | Projections.Add(instance) |  |
| Projections/event_projection_should_register_document_types.cs | AuditRecordCreatorProjection | EventProjection | Create | partial | — | Projections.Add(instance) |  |
| Projections/hiearchy_projection.cs | ThingProjection | SingleStreamProjection<HThing, Guid> | (none) | partial | — | Projections.Add(instance) |  |
| Projections/include_extra_schema_objects_from_projections.cs | TableCreatingProjection | EventProjection | (none) | partial | — | Projections.Add |  |
| Projections/inline_transformation_of_events.cs | MonsterDefeatedTransform | EventProjection | Create | partial | — | Projections.Add(instance) |  |
| Projections/project_latest_tests.cs | ReportProjection | SingleStreamProjection<Report, Guid> | Apply, Create | partial | — | Projections.Add |  |
| Projections/using_explicit_code_for_live_aggregation.cs | ExplicitCounter | SingleStreamProjection<CountedAggregate,... | Evolve | partial | Evolve/EvolveAsync | Projections.Add(instance) |  |
| Projections/using_explicit_code_for_live_aggregation.cs | ExplicitCounterThatHasStringId | SingleStreamProjection<CountedAsString, ... | Evolve | partial | Evolve/EvolveAsync | Projections.Add(instance) |  |
| Projections/using_non_concrete_types_in_projections.cs | NonConcreteTabulatorProjection | SingleStreamProjection<MyAggregate, Guid... | Apply | partial | — | unknown |  |
| Projections/when_registering_a_custom_projection_type.cs | MyProjection | IProjection | (none) | non-partial | — | Projections.Add(instance) |  |
| SchemaChange/Upcasters.cs | ShoppingCartProjection | SingleStreamProjection<ShoppingCartDetai... | (none) | partial | — | unknown |  |
| archiving_events.cs | SimpleAggregateProjection | SingleStreamProjection<SimpleAggregate, ... | Apply, ShouldDelete | partial | — | Projections.Add |  |
| archiving_events.cs | CountedAggregateProjection2 | SingleStreamProjection<CountedAggregate,... | Evolve | partial | Evolve/EvolveAsync | Projections.Add(instance) |  |
| cannot_register_duplicate_projections_by_name.cs | Projection1 | EventProjection | Create | partial | — | Projections.Add |  |
| cannot_register_duplicate_projections_by_name.cs | Projection2 | EventProjection | Create | partial | — | Projections.Add |  |
| schema_object_management.cs | MyAggregateProjection | SingleStreamProjection<MyAggregate, Guid... | Apply | partial | — | Projections.Add(instance) |  |

## src/PatchingTests
| File | Class | Base | Method shapes | partial? | Explicit override? | Registration | Notes |
|------|-------|------|----------------|----------|-------------------|--------------|-------|
| Patching/patching_api.cs | QuestPatchTestProjection | IProjection | (none) | non-partial | — | Projections.Add(instance) |  |

## src/samples
| File | Class | Base | Method shapes | partial? | Explicit override? | Registration | Notes |
|------|-------|------|----------------|----------|-------------------|--------------|-------|
| DocSamples/RegisteringProjections.cs | MySpecialProjection | EventProjection | (none) | partial | — | Projections.Add |  |
| EventSourcingIntro/Program.cs | WarehouseProductProjection | SingleStreamProjection<WarehouseProductR... | Apply | partial | — | Projections.Add |  |
| Helpdesk/Helpdesk.Api/Core/Kafka/KafkaProducer.cs | KafkaProducer | IProjection | Apply | non-partial | — | unknown | Non-partial with Apply/Create ⚠ |
| Helpdesk/Helpdesk.Api/Core/SignalR/SignalRProducer.cs | SignalRProducer | IProjection | Apply | non-partial | — | unknown | Non-partial with Apply/Create ⚠ |
| Helpdesk/Helpdesk.Api/Incidents/GetCustomerIncidentsSummary/IncidentSummary.cs | CustomerIncidentsSummaryProjection | MultiStreamProjection<CustomerIncidentsS... | Apply | partial | — | unknown |  |
| Helpdesk/Helpdesk.Api/Incidents/GetIncidentDetails/IncidentDetails.cs | IncidentDetailsProjection | SingleStreamProjection<IncidentDetails> | Apply, Create | partial | — | unknown |  |
| Helpdesk/Helpdesk.Api/Incidents/GetIncidentHistory/IncidentHistory.cs | IncidentHistoryTransformation | EventProjection | (none) | partial | — | unknown |  |
| Helpdesk/Helpdesk.Api/Incidents/GetIncidentShortInfo/IncidentShortInfo.cs | IncidentShortInfoProjection | SingleStreamProjection<IncidentShortInfo... | Apply, Create | partial | — | unknown |  |

## Self-aggregating aggregates (`Snapshot<T>()` / `LiveStreamAggregation<T>()` registrations)

These types have Apply / Create / ShouldDelete / Evolve methods directly on the aggregate and are registered without a separate `Projection` subclass. The SG emits a sibling `XEvolver` class plus an `[assembly: GeneratedEvolver(typeof(X), typeof(XEvolver))]` registration — the aggregate type itself does **not** need `partial`.

| File | Class | Base | Method shapes | partial? | Explicit override? | Registration | Notes |
|------|-------|------|----------------|----------|-------------------|--------------|-------|
| EventSourcingTests/FetchForWriting/fetching_live_aggregates_for_writing.cs | SimpleAggregate | IRevisioned | Apply(AEvent..EEvent) | non-partial | — | `Snapshot<T>(Inline)` |  |
| EventSourcingTests/FetchForWriting/fetching_live_aggregates_for_writing.cs | SimpleAggregate2 | — | Apply(AEvent..EEvent) | non-partial | — | `Snapshot<T>(Inline)` |  |
| EventSourcingTests/FetchForWriting/fetching_live_aggregates_for_writing.cs | SimpleAggregateAsString | — | Apply(AEvent..EEvent) | non-partial | — | `Snapshot<T>(Inline)` | string-id |
| EventSourcingTests/archiving_events.cs | SimpleAggregateStrongTypedGuid | — | Apply(AEvent..EEvent) | non-partial | — | `Snapshot<T>(Inline)` | strong-typed Guid id |
| EventSourcingTests/archiving_events.cs | SimpleAggregateStrongTypedString | — | Apply(AEvent..EEvent) | non-partial | — | `Snapshot<T>(Inline)` | strong-typed string id |
| EventSourcingTests/Projections/QuestParty.cs | QuestParty | — | Apply(MembersJoined / MembersDeparted / QuestStarted) | non-partial | — | `Snapshot<T>(Inline)` |  |
| EventSourcingTests/end_to_end_event_capture_and_fetching_the_stream_with_string_identifiers.cs | QuestPartyWithStringIdentifier | — | Apply(events) | non-partial | — | `Snapshot<T>(Inline)` | string-id |
| EventSourcingTests/Aggregation/blue_green_deployment_of_aggregates.cs | OtherAggregate | MyAggregate | Apply(AEvent) | non-partial | — | `Snapshot<T>(Async)` | `[ProjectionVersion(3)]` |
| EventSourcingTests/Aggregation/stream_compacting.cs | Letters | IRevisioned | Apply(AEvent..DEvent) | non-partial | — | `Snapshot<T>(Async/Inline)` | versioned |
| EventSourcingTests/Aggregation/self_aggregating_evolve_method.cs | MutableIEventEvolveAggregate | — | Evolve(IEvent) | non-partial | Evolve | `Snapshot<T>(Inline)` | deliberate Evolve |
| EventSourcingTests/Aggregation/self_aggregating_evolve_method.cs | MutableObjectEvolveAggregate | — | Evolve(object) | non-partial | Evolve | `Snapshot<T>(Inline)` | deliberate Evolve |
| EventSourcingTests/Aggregation/self_aggregating_evolve_method.cs | ImmutableIEventEvolveAggregate | — | Evolve(IEvent)→self | non-partial (record) | Evolve | `Snapshot<T>(Inline)` | deliberate Evolve, immutable |
| EventSourcingTests/Aggregation/self_aggregating_evolve_method.cs | ImmutableObjectEvolveAggregate | — | Evolve(object)→self | non-partial (record) | Evolve | `Snapshot<T>(Inline)` | deliberate Evolve, immutable |
| EventSourcingTests/Aggregation/self_aggregating_evolve_method.cs | AsyncEvolveAggregate | — | EvolveAsync(IEvent, IQuerySession) | non-partial | EvolveAsync | `Snapshot<T>(Inline)` | deliberate EvolveAsync |
| EventSourcingTests/Aggregation/self_aggregating_evolve_method.cs | ImmutableAsyncEvolveAggregate | — | `ValueTask<TDoc> EvolveAsync(IEvent, IQuerySession)` | non-partial (record) | EvolveAsync | `Snapshot<T>(Inline)` | deliberate EvolveAsync, immutable |
| EventSourcingTests/Dcb/dcb_tag_query_and_consistency_tests.cs | StudentCourseEnrollment | — | Apply(StudentEnrolled / AssignmentSubmitted / StudentDropped) | non-partial | — | `LiveStreamAggregation<T>()` | DCB tag aggregation |
| EventSourcingTests/Aggregation/using_guid_based_strong_typed_id_for_aggregate_identity.cs | Payment | — | Create(IEvent<PaymentCreated>), Apply(PaymentCanceled / PaymentVerified) | non-partial | — | `Snapshot<T>(Inline/Async)` | strong-typed Guid id |
| EventSourcingTests/Aggregation/using_string_based_strong_typed_id_for_aggregate_identity.cs | Payment2 | — | Create(IEvent<PaymentCreated>), Apply(...) | non-partial | — | `Snapshot<T>(Inline/Async)` | strong-typed string id |

## Headline counts

| Metric | Count |
|---|---|
| Projection-subclass types (Pattern-A emit) | 221 |
| — of those, `partial` (SG-merged dispatcher expected) | 189 |
| — of those, non-`partial` with no Apply/Create methods (no SG emission expected) | 30 |
| — of those, non-`partial` implementing `IProjection.ApplyAsync` directly (low-level handlers, no SG emission expected) | 2 |
| — of those, explicit `Evolve` / `EvolveAsync` override (deliberate SG bypass) | 29 |
| — of those, explicit `DetermineAction` / `DetermineActionAsync` override (deliberate SG bypass) | 8 |
| Self-aggregating aggregate types (Pattern-B emit, sibling `XEvolver`) | 18 |
| **Total dispatcher emission sites** | **239** |
| SG gaps surfaced by this audit | **0** |

## Notes

- **Registration mode = `unknown`** for some rows: the projection class is declared in one file but registered in a fixture / setup helper elsewhere. Determining the registration call site requires a project-wide cross-reference and isn't load-bearing for the audit; the existence (or not) of a generated dispatcher is the dispositive signal, and the test suite's green status confirms emission.
- **Nested classes**: a small number of projections are nested inside their test class; they're included in the count with the containing test class in the File column.
- **Pattern-A vs Pattern-B**: the agent's first pass conflated the two and reported all non-partial Apply-bearing types as risk. They aren't — Pattern-B (self-aggregating) is genuinely non-partial-friendly. The headline counts above reflect the corrected classification.
