```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Intel Core i9-14900K, 1 CPU, 32 logical and 24 physical cores
.NET SDK 9.0.306
  [Host]           : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2
  EnvConfiguredJob : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                              | StackingFrameCount | FrameWidth | SyntheticFilterCount | Mean     | Error    | StdDev   | Gen0      | Gen1      | Gen2      | Allocated |
|------------------------------------ |------------------- |----------- |--------------------- |---------:|---------:|---------:|----------:|----------:|----------:|----------:|
| **&#39;Capture + stack + filter pipeline&#39;** | **1**                  | **1024**       | **2**                    | **46.24 ms** | **0.930 ms** | **0.615 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **16.11 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **1**                  | **1024**       | **4**                    | **64.55 ms** | **1.141 ms** | **0.679 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **16.19 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **4**                  | **1024**       | **2**                    | **61.06 ms** | **2.255 ms** | **1.492 ms** | **2000.0000** | **2000.0000** | **2000.0000** |   **24.1 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **4**                  | **1024**       | **4**                    | **79.98 ms** | **1.953 ms** | **1.292 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **24.18 MB** |
