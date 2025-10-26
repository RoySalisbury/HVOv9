```

BenchmarkDotNet v0.15.4, Linux Debian GNU/Linux 12 (bookworm) (container)
-
.NET SDK 9.0.304
  [Host]           : .NET 9.0.8 (9.0.8, 9.0.825.36511), Arm64 RyuJIT armv8.0-a
  EnvConfiguredJob : .NET 9.0.8 (9.0.8, 9.0.825.36511), Arm64 RyuJIT armv8.0-a

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                        | StackingFrameCount | Mean     | Error    | StdDev   | Gen0      | Gen1      | Gen2      | Allocated |
|---------------------------------------------- |------------------- |---------:|---------:|---------:|----------:|----------:|----------:|----------:|
| **&#39;Accumulate single frame into rolling buffer&#39;** | **1**                  | **23.90 ms** | **0.163 ms** | **0.097 ms** | **1000.0000** | **1000.0000** | **1000.0000** |  **15.82 MB** |
| **&#39;Accumulate single frame into rolling buffer&#39;** | **4**                  | **31.50 ms** | **0.637 ms** | **0.422 ms** | **1312.5000** | **1312.5000** | **1312.5000** |  **31.64 MB** |
| **&#39;Accumulate single frame into rolling buffer&#39;** | **8**                  | **40.84 ms** | **0.842 ms** | **0.557 ms** | **1307.6923** | **1307.6923** | **1307.6923** |  **31.64 MB** |
