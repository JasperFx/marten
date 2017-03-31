``` ini

BenchmarkDotNet=v0.10.1, OS=Microsoft Windows NT 6.1.7601 Service Pack 1
Processor=Intel(R) Core(TM) i7-4980HQ CPU 2.80GHz, ProcessorCount=4
Frequency=10000000 Hz, Resolution=100.0000 ns, Timer=UNKNOWN
  [Host]     : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1076.0
  Job-DNINOD : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1076.0

WarmupCount=2  

```
            Method |            Mean |        StdDev |     Gen 0 |   Gen 1 | Allocated |
------------------ |---------------- |-------------- |---------- |-------- |---------- |
 CreateLinqCommand |     189.7593 us |     2.3160 us |         - |       - |  42.64 kB |
      RunLinqQuery | 124,248.2148 us | 1,785.5957 us | 4975.0000 |       - |  34.13 MB |
   CompiledQueries |  29,998.1235 us |   719.6757 us |  958.3333 | 83.3333 |   8.71 MB |
