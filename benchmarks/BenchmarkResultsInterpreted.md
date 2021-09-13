# Benchmark results (interpreted)

```
BenchmarkDotNet=v0.13.1, OS=Windows 10.0.19042.1165 (20H2/October2020Update)
Intel Core i7-7700HQ CPU 2.80GHz (Kaby Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK=6.0.100-rc.2.21456.8
  [Host]     : .NET 6.0.0 (6.0.21.45401), X64 RyuJIT
  DefaultJob : .NET 6.0.0 (6.0.21.45401), X64 RyuJIT
```

**Fastest**, *Slowest*, Other

|           Method |  Size | IterCount |          Mean |       Error |      StdDev |        Median |
|----------------- |------ |---------- |--------------:|------------:|------------:|--------------:|
|     TestMimAlloc |   100 |         1 |     33.878 ns |   0.4356 ns |   0.3637 ns |     33.857 ns |
|  **TestGCAlloc** |   100 |         1 |      9.907 ns |   0.1916 ns |   0.1792 ns |      9.868 ns |
|  TestNativeAlloc |   100 |         1 |     67.508 ns |   0.4844 ns |   0.4531 ns |     67.457 ns |
| *TestAllocHGlobal* |   100 |         1 |     76.982 ns |   0.9527 ns |   0.8912 ns |     76.801 ns |

|           Method |  Size | IterCount |          Mean |       Error |      StdDev |        Median |
|----------------- |------ |---------- |--------------:|------------:|------------:|--------------:|
|     TestMimAlloc |   100 |         5 |    185.591 ns |   1.3812 ns |   1.2920 ns |    185.432 ns |
|  **TestGCAlloc** |   100 |         5 |     49.252 ns |   1.0033 ns |   2.4230 ns |     48.039 ns |
|  TestNativeAlloc |   100 |         5 |    309.583 ns |   2.5734 ns |   2.2813 ns |    309.960 ns |
| *TestAllocHGlobal* |   100 |         5 |    369.274 ns |   3.2756 ns |   3.0640 ns |    369.336 ns |

|           Method |  Size | IterCount |          Mean |       Error |      StdDev |        Median |
|----------------- |------ |---------- |--------------:|------------:|------------:|--------------:|
|     TestMimAlloc |   100 |        40 |  1,424.181 ns |   6.8748 ns |   5.7407 ns |  1,424.507 ns |
|  **TestGCAlloc** |   100 |        40 |    425.878 ns |   8.5370 ns |  16.0346 ns |    420.189 ns |
|  TestNativeAlloc |   100 |        40 |  2,428.679 ns |  14.9990 ns |  13.2963 ns |  2,427.448 ns |
| *TestAllocHGlobal* |   100 |        40 |  2,860.819 ns |  19.4733 ns |  18.2153 ns |  2,861.133 ns |

|           Method |  Size | IterCount |          Mean |       Error |      StdDev |        Median |
|----------------- |------ |---------- |--------------:|------------:|------------:|--------------:|
| **TestMimAlloc** | 10000 |         1 |     52.519 ns |   0.2722 ns |   0.2125 ns |     52.532 ns |
|      *TestGCAlloc* | 10000 |         1 |    420.413 ns |   8.2738 ns |  11.5987 ns |    418.140 ns |
|  TestNativeAlloc | 10000 |         1 |     64.891 ns |   0.3773 ns |   0.3529 ns |     64.832 ns |
| TestAllocHGlobal | 10000 |         1 |     75.665 ns |   0.7461 ns |   0.6979 ns |     75.727 ns |

|           Method |  Size | IterCount |          Mean |       Error |      StdDev |        Median |
|----------------- |------ |---------- |--------------:|------------:|------------:|--------------:|
| **TestMimAlloc** | 10000 |         5 |    260.024 ns |   3.1886 ns |   2.9826 ns |    259.569 ns |
|      *TestGCAlloc* | 10000 |         5 |  2,547.774 ns |  39.6015 ns |  37.0433 ns |  2,549.163 ns |
|  TestNativeAlloc | 10000 |         5 |    319.688 ns |   2.1862 ns |   2.0450 ns |    319.809 ns |
| TestAllocHGlobal | 10000 |         5 |    367.003 ns |   2.9486 ns |   2.6139 ns |    366.703 ns |

|           Method |  Size | IterCount |          Mean |       Error |      StdDev |        Median |
|----------------- |------ |---------- |--------------:|------------:|------------:|--------------:|
| **TestMimAlloc** | 10000 |        40 |  2,005.173 ns |  17.3577 ns |  15.3871 ns |  2,004.121 ns |
|      *TestGCAlloc* | 10000 |        40 | 18,858.668 ns | 370.4593 ns | 969.4273 ns | 18,456.676 ns |
|  TestNativeAlloc | 10000 |        40 |  2,550.140 ns |  27.4359 ns |  25.6636 ns |  2,545.568 ns |
| TestAllocHGlobal | 10000 |        40 |  2,848.327 ns |  19.9100 ns |  17.6497 ns |  2,848.032 ns |
