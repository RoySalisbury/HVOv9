```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.304
  [Host]           : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                         | FilterCount | FrameWidth | Mean      | Error     | StdDev    | Gen0     | Gen1     | Gen2     | Allocated |
|----------------------------------------------- |------------ |----------- |----------:|----------:|----------:|---------:|---------:|---------:|----------:|
| **&#39;Process stacked frame with synthetic filters&#39;** | **1**           | **512**        |  **6.169 ms** | **0.2339 ms** | **0.1547 ms** | **992.1875** | **992.1875** | **992.1875** |   **3.01 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **1**           | **1024**       | **23.013 ms** | **0.3049 ms** | **0.1595 ms** | **968.7500** | **968.7500** | **968.7500** |  **12.02 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **3**           | **512**        |  **9.306 ms** | **0.2991 ms** | **0.1780 ms** | **984.3750** | **984.3750** | **984.3750** |   **3.02 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **3**           | **1024**       | **33.743 ms** | **1.0172 ms** | **0.6728 ms** | **933.3333** | **933.3333** | **933.3333** |  **12.03 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **5**           | **512**        | **13.358 ms** | **0.5618 ms** | **0.3716 ms** | **984.3750** | **984.3750** | **984.3750** |   **3.04 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **5**           | **1024**       | **42.508 ms** | **1.6402 ms** | **0.9760 ms** | **916.6667** | **916.6667** | **916.6667** |  **12.04 MB** |
