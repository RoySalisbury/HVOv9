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
| **&#39;Process stacked frame with synthetic filters&#39;** | **1**           | **512**        |  **5.656 ms** | **0.0975 ms** | **0.0510 ms** | **992.1875** | **992.1875** | **992.1875** |   **3.01 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **1**           | **1024**       | **25.366 ms** | **2.0796 ms** | **1.3755 ms** | **968.7500** | **968.7500** | **968.7500** |  **12.02 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **3**           | **512**        |  **8.730 ms** | **0.0590 ms** | **0.0308 ms** | **984.3750** | **984.3750** | **984.3750** |   **3.02 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **3**           | **1024**       | **33.979 ms** | **0.5557 ms** | **0.3307 ms** | **933.3333** | **933.3333** | **933.3333** |  **12.03 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **5**           | **512**        | **13.325 ms** | **1.2135 ms** | **0.7221 ms** | **984.3750** | **984.3750** | **984.3750** |   **3.04 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **5**           | **1024**       | **45.585 ms** | **1.9058 ms** | **1.2606 ms** | **916.6667** | **916.6667** | **916.6667** |  **12.04 MB** |
