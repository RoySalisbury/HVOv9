```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.304
  [Host]           : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                        | StackingFrameCount | Mean     | Error    | StdDev   | Gen0      | Gen1      | Gen2      | Allocated |
|---------------------------------------------- |------------------- |---------:|---------:|---------:|----------:|----------:|----------:|----------:|
| **&#39;Accumulate single frame into rolling buffer&#39;** | **1**                  | **24.22 ms** | **0.826 ms** | **0.546 ms** | **1000.0000** | **1000.0000** | **1000.0000** |  **15.82 MB** |
| **&#39;Accumulate single frame into rolling buffer&#39;** | **4**                  | **31.84 ms** | **1.631 ms** | **0.971 ms** | **1312.5000** | **1312.5000** | **1312.5000** |  **31.64 MB** |
| **&#39;Accumulate single frame into rolling buffer&#39;** | **8**                  | **40.52 ms** | **0.689 ms** | **0.410 ms** | **1307.6923** | **1307.6923** | **1307.6923** |  **31.64 MB** |
