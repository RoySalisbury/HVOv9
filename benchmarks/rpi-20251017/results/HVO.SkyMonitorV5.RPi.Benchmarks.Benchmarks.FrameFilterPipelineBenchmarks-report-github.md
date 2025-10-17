```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.306
  [Host]           : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                         | FilterCount | FrameWidth | Mean      | Error    | StdDev   | Gen0     | Gen1     | Gen2     | Allocated |
|----------------------------------------------- |------------ |----------- |----------:|---------:|---------:|---------:|---------:|---------:|----------:|
| **&#39;Process stacked frame with synthetic filters&#39;** | **1**           | **512**        |  **23.19 ms** | **0.202 ms** | **0.120 ms** | **968.7500** | **968.7500** | **968.7500** |   **3.01 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **1**           | **1024**       |  **90.77 ms** | **1.767 ms** | **1.169 ms** | **833.3333** | **833.3333** | **833.3333** |  **12.02 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **3**           | **512**        |  **35.58 ms** | **0.406 ms** | **0.269 ms** | **928.5714** | **928.5714** | **928.5714** |   **3.02 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **3**           | **1024**       | **132.57 ms** | **1.028 ms** | **0.680 ms** | **750.0000** | **750.0000** | **750.0000** |  **12.03 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **5**           | **512**        |  **50.54 ms** | **0.143 ms** | **0.094 ms** | **900.0000** | **900.0000** | **900.0000** |   **3.04 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **5**           | **1024**       | **176.15 ms** | **1.253 ms** | **0.829 ms** | **666.6667** | **666.6667** | **666.6667** |  **12.04 MB** |
