# Basic NOT node comparisons on a single integer property

-> id = 4574a4a5-0ecc-4e20-a9b2-04339126a793
-> lifecycle = Acceptance
-> max-retries = 0
-> last-updated = 2017-10-17T12:51:07.7426468Z
-> tags = 

[Linq]
|> TheDocumentsAre
    [Rows]
    -> String = False
    -> Long = False
    |Name|Number|Flag |Double|Decimal|Date |InnerFlag|
    |A   |1     |false|1     |1      |TODAY|False    |
    |B   |2     |false|1     |1      |TODAY|False    |
    |C   |3     |false|1     |1      |TODAY|False    |
    |D   |4     |false|1     |1      |TODAY|False    |
    |E   |5     |false|1     |1      |TODAY|False    |
    |F   |6     |false|1     |1      |TODAY|False    |

|> ExecutingQuery
    [table]
    |WhereClause      |Results      |
    |Not x.Number == 1|B, C, D, E, F|
    |Not x.Number > 3 |A, B, C      |
    |Not x.Number < 3 |C, D, E, F   |
    |Not x.Number <= 3|D, E, F      |
    |Not x.Number >= 3|A, B         |
    |Not x.Number != 3|C            |

~~~
