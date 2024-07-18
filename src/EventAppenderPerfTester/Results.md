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
