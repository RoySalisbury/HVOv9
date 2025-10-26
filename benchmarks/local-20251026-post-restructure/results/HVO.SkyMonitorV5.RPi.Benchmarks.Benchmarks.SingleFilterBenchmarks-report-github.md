```

BenchmarkDotNet v0.15.4, Linux Debian GNU/Linux 12 (bookworm) (container)
-
.NET SDK 9.0.304
  [Host]           : .NET 9.0.8 (9.0.8, 9.0.825.36511), Arm64 RyuJIT armv8.0-a
  EnvConfiguredJob : .NET 9.0.8 (9.0.8, 9.0.825.36511), Arm64 RyuJIT armv8.0-a

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                           | FrameWidth | Mean       | Error    | StdDev   | Allocated |
|--------------------------------- |----------- |-----------:|---------:|---------:|----------:|
| **&#39;Apply synthetic overlay filter&#39;** | **512**        |   **915.2 μs** | **23.89 μs** | **15.80 μs** |     **832 B** |
| **&#39;Apply synthetic overlay filter&#39;** | **1024**       | **2,324.6 μs** | **40.57 μs** | **24.14 μs** |     **832 B** |
