```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.306
  [Host]           : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                              | StackingFrameCount | FrameWidth | SyntheticFilterCount | Mean     | Error    | StdDev   | Gen0      | Gen1      | Gen2      | Allocated |
|------------------------------------ |------------------- |----------- |--------------------- |---------:|---------:|---------:|----------:|----------:|----------:|----------:|
| **&#39;Capture + stack + filter pipeline&#39;** | **1**                  | **1024**       | **2**                    | **35.47 ms** | **0.799 ms** | **0.418 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **16.11 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **1**                  | **1024**       | **4**                    | **49.28 ms** | **1.907 ms** | **1.135 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **16.19 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **4**                  | **1024**       | **2**                    | **42.05 ms** | **3.683 ms** | **2.436 ms** | **2000.0000** | **2000.0000** | **2000.0000** |   **24.1 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **4**                  | **1024**       | **4**                    | **58.65 ms** | **3.289 ms** | **1.957 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **24.18 MB** |
