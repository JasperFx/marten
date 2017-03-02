``` ini

BenchmarkDotNet=v0.10.1, OS=Microsoft Windows NT 6.1.7601 Service Pack 1
Processor=Intel(R) Core(TM) i7-4980HQ CPU 2.80GHz, ProcessorCount=4
Frequency=10000000 Hz, Resolution=100.0000 ns, Timer=UNKNOWN
  [Host]     : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1076.0
  Job-DNINOD : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1076.0

WarmupCount=2  Gen 0=834.5588  Allocated=7.74 MB  

```
              Method |        Mean |    StdDev |
-------------------- |------------ |---------- |
 BulkInsertDocuments | 197.7231 ms | 5.4325 ms |
