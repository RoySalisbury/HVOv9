```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.306
  [Host]           : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                | OverlayCount | FrameWidth | Mean     | Error   | StdDev  | Allocated |
|-------------------------------------- |------------- |----------- |---------:|--------:|--------:|----------:|
| **&#39;Compose overlays onto stacked frame&#39;** | **1**            | **1024**       | **161.1 ms** | **0.56 ms** | **0.33 ms** |   **1.43 KB** |
| **&#39;Compose overlays onto stacked frame&#39;** | **3**            | **1024**       | **166.0 ms** | **0.51 ms** | **0.27 ms** |   **2.11 KB** |
| **&#39;Compose overlays onto stacked frame&#39;** | **5**            | **1024**       | **164.0 ms** | **0.39 ms** | **0.26 ms** |   **2.53 KB** |
