```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.304
  [Host]           : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                           | FrameWidth | Mean       | Error     | StdDev   | Allocated |
|--------------------------------- |----------- |-----------:|----------:|---------:|----------:|
| **&#39;Apply synthetic overlay filter&#39;** | **512**        |   **935.6 μs** |  **67.97 μs** | **40.45 μs** |     **833 B** |
| **&#39;Apply synthetic overlay filter&#39;** | **1024**       | **2,409.3 μs** | **133.56 μs** | **88.34 μs** |     **835 B** |
