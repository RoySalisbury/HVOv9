```

BenchmarkDotNet v0.15.4, Linux Debian GNU/Linux 12 (bookworm) (container)
-
.NET SDK 9.0.304
  [Host]           : .NET 9.0.8 (9.0.8, 9.0.825.36511), Arm64 RyuJIT armv8.0-a
  EnvConfiguredJob : .NET 9.0.8 (9.0.8, 9.0.825.36511), Arm64 RyuJIT armv8.0-a

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                | OverlayCount | FrameWidth | Mean     | Error    | StdDev   | Allocated |
|-------------------------------------- |------------- |----------- |---------:|---------:|---------:|----------:|
| **&#39;Compose overlays onto stacked frame&#39;** | **1**            | **1024**       | **70.89 ms** | **5.032 ms** | **3.328 ms** |   **1.31 KB** |
| **&#39;Compose overlays onto stacked frame&#39;** | **3**            | **1024**       | **72.77 ms** | **0.607 ms** | **0.401 ms** |   **1.86 KB** |
| **&#39;Compose overlays onto stacked frame&#39;** | **5**            | **1024**       | **73.64 ms** | **0.686 ms** | **0.359 ms** |   **2.41 KB** |
