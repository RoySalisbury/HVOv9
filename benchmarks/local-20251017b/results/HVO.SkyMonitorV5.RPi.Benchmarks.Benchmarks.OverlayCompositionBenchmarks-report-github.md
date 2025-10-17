```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.304
  [Host]           : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                | OverlayCount | FrameWidth | Mean     | Error    | StdDev   | Allocated |
|-------------------------------------- |------------- |----------- |---------:|---------:|---------:|----------:|
| **&#39;Compose overlays onto stacked frame&#39;** | **1**            | **1024**       | **68.92 ms** | **1.074 ms** | **0.639 ms** |    **1.4 KB** |
| **&#39;Compose overlays onto stacked frame&#39;** | **3**            | **1024**       | **74.44 ms** | **2.850 ms** | **1.885 ms** |   **1.96 KB** |
| **&#39;Compose overlays onto stacked frame&#39;** | **5**            | **1024**       | **73.95 ms** | **0.930 ms** | **0.554 ms** |   **2.51 KB** |
