
BenchmarkDotNet=v0.10.1, OS=Microsoft Windows NT 6.1.7601 Service Pack 1
Processor=Intel(R) Core(TM) i7-4980HQ CPU 2.80GHz, ProcessorCount=4
Frequency=10000000 Hz, Resolution=100.0000 ns, Timer=UNKNOWN
  [Host]     : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1076.0
  Job-DNINOD : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1076.0

WarmupCount=2  

            Method |            Mean |        StdDev |     Gen 0 |   Gen 1 | Allocated |
------------------ |---------------- |-------------- |---------- |-------- |---------- |
 CreateLinqCommand |     179.9882 us |     6.0692 us |    2.6693 |       - |  45.11 kB |
      RunLinqQuery | 112,916.1908 us | 2,069.6093 us | 4925.0000 |       - |  34.03 MB |
   CompiledQueries |  27,084.0787 us |   210.1911 us |  941.6667 | 66.6667 |    8.7 MB |
