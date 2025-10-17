```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Intel Core i9-14900K, 1 CPU, 32 logical and 24 physical cores
.NET SDK 9.0.306
  [Host]           : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2
  EnvConfiguredJob : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                        | StackingFrameCount | Mean     | Error    | StdDev   | Gen0      | Gen1      | Gen2      | Allocated |
|---------------------------------------------- |------------------- |---------:|---------:|---------:|----------:|----------:|----------:|----------:|
| **&#39;Accumulate single frame into rolling buffer&#39;** | **1**                  | **27.30 ms** | **0.449 ms** | **0.297 ms** | **1000.0000** | **1000.0000** | **1000.0000** |  **15.82 MB** |
| **&#39;Accumulate single frame into rolling buffer&#39;** | **4**                  | **49.97 ms** | **2.492 ms** | **1.648 ms** | **1272.7273** | **1272.7273** | **1272.7273** |  **31.64 MB** |
| **&#39;Accumulate single frame into rolling buffer&#39;** | **8**                  | **71.28 ms** | **3.101 ms** | **2.051 ms** | **1250.0000** | **1250.0000** | **1250.0000** |  **31.64 MB** |
