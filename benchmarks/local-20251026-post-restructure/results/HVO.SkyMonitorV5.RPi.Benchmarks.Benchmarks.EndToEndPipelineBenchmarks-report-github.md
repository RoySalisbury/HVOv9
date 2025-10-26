```

BenchmarkDotNet v0.15.4, Linux Debian GNU/Linux 12 (bookworm) (container)
-
.NET SDK 9.0.304
  [Host]           : .NET 9.0.8 (9.0.8, 9.0.825.36511), Arm64 RyuJIT armv8.0-a
  EnvConfiguredJob : .NET 9.0.8 (9.0.8, 9.0.825.36511), Arm64 RyuJIT armv8.0-a

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                              | StackingFrameCount | FrameWidth | SyntheticFilterCount | Mean     | Error    | StdDev   | Gen0      | Gen1      | Gen2      | Allocated |
|------------------------------------ |------------------- |----------- |--------------------- |---------:|---------:|---------:|----------:|----------:|----------:|----------:|
| **&#39;Capture + stack + filter pipeline&#39;** | **1**                  | **1024**       | **2**                    | **35.03 ms** | **0.371 ms** | **0.221 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **16.11 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **1**                  | **1024**       | **4**                    | **48.26 ms** | **1.068 ms** | **0.635 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **16.18 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **4**                  | **1024**       | **2**                    | **43.30 ms** | **1.901 ms** | **1.132 ms** | **2000.0000** | **2000.0000** | **2000.0000** |   **24.1 MB** |
| **&#39;Capture + stack + filter pipeline&#39;** | **4**                  | **1024**       | **4**                    | **53.98 ms** | **1.744 ms** | **1.153 ms** | **2000.0000** | **2000.0000** | **2000.0000** |  **24.18 MB** |
