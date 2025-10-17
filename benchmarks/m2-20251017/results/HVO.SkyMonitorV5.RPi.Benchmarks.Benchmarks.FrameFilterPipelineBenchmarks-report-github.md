```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Intel Core i9-14900K, 1 CPU, 32 logical and 24 physical cores
.NET SDK 9.0.306
  [Host]           : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2
  EnvConfiguredJob : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                         | FilterCount | FrameWidth | Mean      | Error     | StdDev    | Gen0     | Gen1     | Gen2     | Allocated |
|----------------------------------------------- |------------ |----------- |----------:|----------:|----------:|---------:|---------:|---------:|----------:|
| **&#39;Process stacked frame with synthetic filters&#39;** | **1**           | **512**        |  **7.849 ms** | **0.1203 ms** | **0.0795 ms** | **984.3750** | **984.3750** | **984.3750** |   **3.01 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **1**           | **1024**       | **31.589 ms** | **0.9185 ms** | **0.6076 ms** | **937.5000** | **937.5000** | **937.5000** |  **12.02 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **3**           | **512**        | **12.225 ms** | **0.2519 ms** | **0.1666 ms** | **984.3750** | **984.3750** | **984.3750** |   **3.02 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **3**           | **1024**       | **46.936 ms** | **1.4018 ms** | **0.9272 ms** | **909.0909** | **909.0909** | **909.0909** |  **12.03 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **5**           | **512**        | **16.758 ms** | **0.1783 ms** | **0.1061 ms** | **968.7500** | **968.7500** | **968.7500** |   **3.04 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **5**           | **1024**       | **61.383 ms** | **0.8922 ms** | **0.5901 ms** | **888.8889** | **888.8889** | **888.8889** |  **12.04 MB** |
