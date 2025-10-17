```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Intel Core i9-14900K, 1 CPU, 32 logical and 24 physical cores
.NET SDK 9.0.306
  [Host]           : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2
  EnvConfiguredJob : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                           | FrameWidth | Mean       | Error    | StdDev   | Allocated |
|--------------------------------- |----------- |-----------:|---------:|---------:|----------:|
| **&#39;Apply synthetic overlay filter&#39;** | **512**        |   **806.4 μs** |  **3.97 μs** |  **2.62 μs** |     **833 B** |
| **&#39;Apply synthetic overlay filter&#39;** | **1024**       | **2,793.4 μs** | **24.15 μs** | **15.97 μs** |     **835 B** |
