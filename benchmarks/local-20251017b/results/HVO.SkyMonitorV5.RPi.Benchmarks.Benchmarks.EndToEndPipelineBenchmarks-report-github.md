```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.304
  [Host]           : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                              | StackingFrameCount | FrameWidth | SyntheticFilterCount | Mean     | Error    | StdDev   | Gen0      | Gen1      | Gen2      | Allocated |
|------------------------------------ |------------------- |----------- |--------------------- |---------:|---------:|---------:|----------:|----------:|----------:|----------:|
| **&#39;Capture + stack + filter pipeline&#39;** | **1**                  | **1024**       | **2**                    | **36.39 ms** | **2.341 ms** | **1.548 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **16.11 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **1**                  | **1024**       | **4**                    | **48.34 ms** | **1.408 ms** | **0.736 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **16.19 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **4**                  | **1024**       | **2**                    | **42.37 ms** | **3.731 ms** | **1.951 ms** | **2000.0000** | **2000.0000** | **2000.0000** |   **24.1 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **4**                  | **1024**       | **4**                    | **53.95 ms** | **2.380 ms** | **1.245 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **24.18 MB** |
