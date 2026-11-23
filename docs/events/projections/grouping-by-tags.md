# Grouping Multi-Stream Projections by Tags <Badge type="tip" text="10.0" />

[DCB tags](/events/dcb) aren't only for consistency boundaries. A multi-stream projection can group its events by a tag
type with `IdentityFromTag<TTag>()`, and that's now the way we'd recommend building most multi-stream projections whose
identity is something like an account, an order or a customer.

## An Example

The tag type is a strong-typed identifier wrapping one value. Here are the tag type and the events for a little billing
system:

<!-- snippet: sample_tag_grouped_projection_events -->
<a id='snippet-sample_tag_grouped_projection_events'></a>
```cs
// The tag type: a strong-typed identifier wrapping one value
public record BillingAccountId(Guid Value);

public record ChargePosted(Guid Account, decimal Amount);

public record PaymentReceived(Guid Account, decimal Amount);

// Names no account at all, so it gets its tags by hand
public record FeeCharged(decimal Amount);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/tag_grouped_projection_samples.cs#L18-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tag_grouped_projection_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Register the tag type, and tell Marten how to tag each event when it's built with `TagWith()` rules. A rule runs on every
append, so you can't forget to tag a charge in some code path:

<!-- snippet: sample_registering_a_tag_grouped_projection -->
<a id='snippet-sample_registering_a_tag_grouped_projection'></a>
```cs
// One tag table, mt_event_tag_billing_account
opts.Events.RegisterTagType<BillingAccountId>("billing_account");

// Tag every charge and payment with its account when the event is built
opts.Events.TagWith<ChargePosted>(e => new BillingAccountId(e.Account));
opts.Events.TagWith<PaymentReceived>(e => new BillingAccountId(e.Account));

opts.Projections.Add<AccountBalanceProjection>(ProjectionLifecycle.Async);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/tag_grouped_projection_samples.cs#L85-L96' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_a_tag_grouped_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The projection groups every event carrying a `BillingAccountId` tag by the tag's value. The projection's identity can be
the tag type itself or the value it wraps, `Guid` here:

<!-- snippet: sample_tag_grouped_projection -->
<a id='snippet-sample_tag_grouped_projection'></a>
```cs
public class AccountBalanceProjection: MultiStreamProjection<AccountBalance, Guid>
{
    public AccountBalanceProjection()
    {
        // Every event carrying a BillingAccountId tag goes to the account the tag names
        IdentityFromTag<BillingAccountId>();
    }

    public void Apply(ChargePosted e, AccountBalance balance) => balance.Balance -= e.Amount;

    public void Apply(PaymentReceived e, AccountBalance balance) => balance.Balance += e.Amount;

    // Applied once to every account the event is tagged with
    public void Apply(FeeCharged e, AccountBalance balance) => balance.Balance -= e.Amount;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/tag_grouped_projection_samples.cs#L38-L56' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tag_grouped_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

An event that a `TagWith()` rule can't tag, because its body doesn't name an account, can be tagged by hand. This fee is
charged to two accounts, so it goes to both documents:

<!-- snippet: sample_tagging_one_event_by_hand -->
<a id='snippet-sample_tagging_one_event_by_hand'></a>
```cs
var fee = session.Events.BuildEvent(new FeeCharged(5m));
fee.WithTag(new BillingAccountId(checking), new BillingAccountId(savings));
session.Events.StartStream(Guid.NewGuid(), fee);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/tag_grouped_projection_samples.cs#L119-L125' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tagging_one_event_by_hand' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Tagging an event with two values of one tag type needs the default `DcbStorageMode.TagTables` storage. `HStore` mode
refuses an event that carries two values of one tag type.

## Why Not an `Identity()` Delegate?

You could write the same projection with `Identity<TEvent>(e => e.Account)` delegates:

<!-- snippet: sample_delegate_grouped_projection -->
<a id='snippet-sample_delegate_grouped_projection'></a>
```cs
public class AccountBalanceByDelegateProjection: MultiStreamProjection<AccountBalance, Guid>
{
    public AccountBalanceByDelegateProjection()
    {
        // Works, but Marten can't run this backwards to find one account's events
        Identity<ChargePosted>(e => e.Account);
        Identity<PaymentReceived>(e => e.Account);
    }

    public void Apply(ChargePosted e, AccountBalance balance) => balance.Balance -= e.Amount;

    public void Apply(PaymentReceived e, AccountBalance balance) => balance.Balance += e.Amount;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/tag_grouped_projection_samples.cs#L58-L74' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_delegate_grouped_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

That builds the same documents, but Marten can only run a delegate forward. It can't turn "account X" back into "the
events for account X", so the only way to find one account's events is to read every event of those types. With
`IdentityFromTag()`, one account's events are a lookup in the tag table, and that buys you:

* **Rebuilding or replaying one identity.** `RebuildIdentityAsync()` re-folds one document from its tagged events,
  without rebuilding the whole projection (see the sample below).
* **Parking instead of pausing.** When your projection code throws on one account's event, the daemon
  [parks that one account](/events/projections/async-daemon#parking-a-failing-identity) and keeps building every other
  one. A delegate-grouped projection can't park, so the same error pauses the whole projection.
* **Stepping through one identity.** The single-identity stepper (`RunMultiStreamProjectionForIdentityAsync()`, which
  CritterWatch uses) reads one document's events straight from the tag table, and a page of them is exact.

<!-- snippet: sample_rebuilding_one_tag_grouped_identity -->
<a id='snippet-sample_rebuilding_one_tag_grouped_identity'></a>
```cs
// Re-folds one account from its tagged events, in the shard that runs the projection
await daemon.RebuildIdentityAsync("AccountBalance", tenantId: null, checking,
    TestContext.Current.CancellationToken);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/tag_grouped_projection_samples.cs#L137-L143' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rebuilding_one_tag_grouped_identity' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`describe` and the daemon's startup log show how each async projection finds one identity's events. The tag-grouped
projection shows as `Tag`. The delegate version shows as `ScanOnly`, with the reason: its `Identity()` delegates can't be
inverted to find one identity's events, so declare the grouping with `IdentityFromStream<TEvent>()` or
`IdentityFromTag<TTag>()`, or turn on the slice index.

Marten never infers a tag grouping from a delegate, even when a `TagWith()` rule tags the same value the delegate picks,
because a delegate can't be compared to anything and a tag can be added by hand. If you can't move to tags, the slice
index (`UseSliceIndex()`) is the other way to make a multi-stream projection park and rebuild by identity. All of this is
described in the JasperFx documentation for
[parking a failing projection identity](https://shared-libs.jasperfx.net/events/parked-identities).

You can mix `IdentityFromTag()` with `IdentityFromStream<TEvent>()` rules in one projection and keep all of the above.
Adding even one `Identity()` delegate makes it `ScanOnly`.

## The Tags Are the Grouping

With `IdentityFromTag()`, the stored tags decide which documents an event goes to, not the event's body. That cuts both
ways:

* An event tagged by hand with an account the `TagWith()` rule wouldn't pick goes to that account too, and stays there:
  the tag is stored with the event, so every rebuild and replay applies it to the same documents again.
* An event of a type the projection handles, but with no tag, isn't applied to any document. That's why we'd rather use
  `TagWith()` rules than tag events at each call site.

## Multi-Tenancy

With conjoined tenancy, tags belong to the event's tenant. The same tag value in two tenants is two documents, and
rebuilding or parking one of them doesn't touch the other:

<!-- snippet: sample_tag_grouped_projection_with_conjoined_tenancy -->
<a id='snippet-sample_tag_grouped_projection_with_conjoined_tenancy'></a>
```cs
opts.Events.TenancyStyle = TenancyStyle.Conjoined;
opts.Schema.For<AccountBalance>().MultiTenanted();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/tag_grouped_projection_samples.cs#L212-L217' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_tag_grouped_projection_with_conjoined_tenancy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The same holds with `UseTenantPartitionedEvents`, where each tenant has its own event sequence: the daemon reads each
event's tags in its own tenant.

Grouping across tenants or rolling up by tenant (`RollUpByTenant()`) makes the projection `ScanOnly`, because one
identity's events no longer live in one tenant. Across tenants, the slice index still works.
