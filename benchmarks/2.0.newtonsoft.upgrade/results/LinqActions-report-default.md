
BenchmarkDotNet=v0.10.1, OS=Microsoft Windows NT 6.1.7601 Service Pack 1
Processor=Intel(R) Core(TM) i7-4980HQ CPU 2.80GHz, ProcessorCount=4
Frequency=10000000 Hz, Resolution=100.0000 ns, Timer=UNKNOWN
  [Host]     : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1076.0
  Job-DNINOD : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1076.0

WarmupCount=2  

            Method |            Mean |        StdErr |        StdDev |     Gen 0 | Allocated |
------------------ |---------------- |-------------- |-------------- |---------- |---------- |
 CreateLinqCommand |     184.7895 us |     0.8073 us |     3.0205 us |    2.0508 |  42.64 kB |
      RunLinqQuery | 131,137.1003 us | 1,521.6670 us | 9,861.5295 us | 4895.8333 |  33.73 MB |
   CompiledQueries |  29,830.5917 us |   219.6355 us |   850.6445 us |  895.8333 |   8.59 MB |
