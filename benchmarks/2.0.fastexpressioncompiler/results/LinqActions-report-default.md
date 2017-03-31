
BenchmarkDotNet=v0.10.1, OS=Microsoft Windows NT 6.1.7601 Service Pack 1
Processor=Intel(R) Core(TM) i7-4980HQ CPU 2.80GHz, ProcessorCount=4
Frequency=10000000 Hz, Resolution=100.0000 ns, Timer=UNKNOWN
  [Host]     : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1076.0
  Job-DNINOD : Clr 4.0.30319.42000, 64bit RyuJIT-v4.6.1076.0

WarmupCount=2  

            Method |            Mean |        StdErr |        StdDev |     Gen 0 | Allocated |
------------------ |---------------- |-------------- |-------------- |---------- |---------- |
 CreateLinqCommand |     197.4190 us |     1.9695 us |    13.9265 us |         - |  42.45 kB |
      RunLinqQuery | 121,335.7528 us | 1,200.0957 us | 4,800.3829 us | 4986.4130 |  33.68 MB |
   CompiledQueries |  30,801.5613 us |   309.4860 us | 3,079.3465 us |  812.5000 |   8.59 MB |
