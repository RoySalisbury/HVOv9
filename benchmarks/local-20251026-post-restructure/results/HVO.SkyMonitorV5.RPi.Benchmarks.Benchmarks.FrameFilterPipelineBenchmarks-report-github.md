```

BenchmarkDotNet v0.15.4, Linux Debian GNU/Linux 12 (bookworm) (container)
-
.NET SDK 9.0.304
  [Host]           : .NET 9.0.8 (9.0.8, 9.0.825.36511), Arm64 RyuJIT armv8.0-a
  EnvConfiguredJob : .NET 9.0.8 (9.0.8, 9.0.825.36511), Arm64 RyuJIT armv8.0-a

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                         | FilterCount | FrameWidth | Mean      | Error     | StdDev    | Gen0     | Gen1     | Gen2     | Allocated |
|----------------------------------------------- |------------ |----------- |----------:|----------:|----------:|---------:|---------:|---------:|----------:|
| **&#39;Process stacked frame with synthetic filters&#39;** | **1**           | **512**        |  **5.948 ms** | **0.0712 ms** | **0.0424 ms** | **992.1875** | **992.1875** | **992.1875** |   **3.01 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **1**           | **1024**       | **23.075 ms** | **0.2557 ms** | **0.1522 ms** | **968.7500** | **968.7500** | **968.7500** |  **12.02 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **3**           | **512**        |  **9.027 ms** | **0.0973 ms** | **0.0644 ms** | **984.3750** | **984.3750** | **984.3750** |   **3.02 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **3**           | **1024**       | **33.691 ms** | **0.3431 ms** | **0.2269 ms** | **933.3333** | **933.3333** | **933.3333** |  **12.03 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **5**           | **512**        | **13.159 ms** | **0.8316 ms** | **0.4949 ms** | **984.3750** | **984.3750** | **984.3750** |   **3.04 MB** |
| **&#39;Process stacked frame with synthetic filters&#39;** | **5**           | **1024**       | **44.536 ms** | **0.6528 ms** | **0.3414 ms** | **916.6667** | **916.6667** | **916.6667** |  **12.04 MB** |
