``` ini

BenchmarkDotNet=v0.10.1, OS=Microsoft Windows NT 6.1.7601 Service Pack 1
Processor=Intel(R) Core(TM) i7-4980HQ CPU 2.80GHz, ProcessorCount=4
Frequency=10000000 Hz, Resolution=100.0000 ns, Timer=UNKNOWN
  [Host]     : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1076.0
  Job-DNINOD : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1076.0

WarmupCount=2  Gen 0=287.5000  Allocated=4.62 MB  

```
          Method |       Mean |    StdDev |
---------------- |----------- |---------- |
 InsertDocuments | 43.7957 ms | 2.6786 ms |
