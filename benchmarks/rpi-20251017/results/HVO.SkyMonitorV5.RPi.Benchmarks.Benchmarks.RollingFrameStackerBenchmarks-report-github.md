```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.306
  [Host]           : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                        | StackingFrameCount | Mean      | Error    | StdDev   | Gen0      | Gen1      | Gen2      | Allocated |
|---------------------------------------------- |------------------- |----------:|---------:|---------:|----------:|----------:|----------:|----------:|
| **&#39;Accumulate single frame into rolling buffer&#39;** | **1**                  |  **95.06 ms** | **0.234 ms** | **0.154 ms** | **1000.0000** | **1000.0000** | **1000.0000** |  **15.82 MB** |
| **&#39;Accumulate single frame into rolling buffer&#39;** | **4**                  | **130.62 ms** | **2.084 ms** | **1.240 ms** | **1250.0000** | **1250.0000** | **1250.0000** |  **31.64 MB** |
| **&#39;Accumulate single frame into rolling buffer&#39;** | **8**                  | **172.09 ms** | **1.432 ms** | **0.852 ms** | **1250.0000** | **1250.0000** | **1250.0000** |  **31.64 MB** |
