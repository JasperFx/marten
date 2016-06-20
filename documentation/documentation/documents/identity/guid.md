<!--Title: Guid Identifiers-->

<div class="alert alert-info">
As of Marten 1.0-alpha, the default Guid mechanism is a sequential or "Comb" Guid. While more expensive to
generate, this makes inserts into the underlying document tables more efficient.
</div>

## CombGuid

To use _CombGuid_ generation you should enabled it when configuring the document store. This defines that the _CombGuid_ generation strategy will be used for all the documents types.

<[sample:configuring-global-sequentialguid]>

It is also possible use the SequentialGuid id generation algorithm for a specific document type.

<[sample:configuring-mapping-specific-sequentialguid]>


