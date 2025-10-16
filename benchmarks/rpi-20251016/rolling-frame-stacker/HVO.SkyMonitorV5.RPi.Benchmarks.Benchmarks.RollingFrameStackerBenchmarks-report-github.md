```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.304
  [Host]           : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=2  LaunchCount=1  
WarmupCount=1  

```
| Method                                        | StackingFrameCount | Mean     | Error | StdDev   | Gen0      | Gen1      | Gen2      | Allocated |
|---------------------------------------------- |------------------- |---------:|------:|---------:|----------:|----------:|----------:|----------:|
| **&#39;Accumulate single frame into rolling buffer&#39;** | **1**                  | **23.43 ms** |    **NA** | **0.806 ms** | **1000.0000** | **1000.0000** | **1000.0000** |  **15.82 MB** |
| **&#39;Accumulate single frame into rolling buffer&#39;** | **4**                  | **30.51 ms** |    **NA** | **0.675 ms** | **1312.5000** | **1312.5000** | **1312.5000** |  **31.64 MB** |
| **&#39;Accumulate single frame into rolling buffer&#39;** | **8**                  | **42.78 ms** |    **NA** | **4.147 ms** | **1272.7273** | **1272.7273** | **1272.7273** |  **31.64 MB** |
