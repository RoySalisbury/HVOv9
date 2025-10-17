```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Intel Core i9-14900K, 1 CPU, 32 logical and 24 physical cores
.NET SDK 9.0.306
  [Host]           : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2
  EnvConfiguredJob : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                | OverlayCount | FrameWidth | Mean     | Error    | StdDev   | Allocated |
|-------------------------------------- |------------- |----------- |---------:|---------:|---------:|----------:|
| **&#39;Compose overlays onto stacked frame&#39;** | **1**            | **1024**       | **43.86 ms** | **0.355 ms** | **0.235 ms** |   **1.37 KB** |
| **&#39;Compose overlays onto stacked frame&#39;** | **3**            | **1024**       | **46.80 ms** | **0.414 ms** | **0.246 ms** |   **1.92 KB** |
| **&#39;Compose overlays onto stacked frame&#39;** | **5**            | **1024**       | **47.49 ms** | **0.754 ms** | **0.499 ms** |   **2.47 KB** |
