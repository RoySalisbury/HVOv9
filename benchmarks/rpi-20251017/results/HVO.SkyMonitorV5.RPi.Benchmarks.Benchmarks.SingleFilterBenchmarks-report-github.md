```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.306
  [Host]           : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                           | FrameWidth | Mean      | Error     | StdDev    | Allocated |
|--------------------------------- |----------- |----------:|----------:|----------:|----------:|
| **&#39;Apply synthetic overlay filter&#39;** | **512**        |  **2.895 ms** | **0.0083 ms** | **0.0043 ms** |     **834 B** |
| **&#39;Apply synthetic overlay filter&#39;** | **1024**       | **10.746 ms** | **0.2188 ms** | **0.1302 ms** |     **839 B** |
