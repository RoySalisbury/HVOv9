```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.306
  [Host]           : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                              | StackingFrameCount | FrameWidth | SyntheticFilterCount | Mean     | Error   | StdDev  | Gen0      | Gen1      | Gen2      | Allocated |
|------------------------------------ |------------------- |----------- |--------------------- |---------:|--------:|--------:|----------:|----------:|----------:|----------:|
| **&#39;Capture + stack + filter pipeline&#39;** | **1**                  | **1024**       | **2**                    | **137.6 ms** | **1.75 ms** | **0.92 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **16.11 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **1**                  | **1024**       | **4**                    | **186.6 ms** | **0.93 ms** | **0.55 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **16.19 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **4**                  | **1024**       | **2**                    | **153.9 ms** | **1.01 ms** | **0.67 ms** | **2000.0000** | **2000.0000** | **2000.0000** |   **24.1 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **4**                  | **1024**       | **4**                    | **208.7 ms** | **1.55 ms** | **1.03 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **24.18 MB** |
