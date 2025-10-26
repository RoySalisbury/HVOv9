```

BenchmarkDotNet v0.15.4, Linux Debian GNU/Linux 12 (bookworm) (container)
-
.NET SDK 9.0.304
  [Host]           : .NET 9.0.8 (9.0.8, 9.0.825.36511), Arm64 RyuJIT armv8.0-a
  EnvConfiguredJob : .NET 9.0.8 (9.0.8, 9.0.825.36511), Arm64 RyuJIT armv8.0-a

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                    | Mean    | Error    | StdDev   | Allocated |
|------------------------------------------ |--------:|---------:|---------:|----------:|
| &#39;Mock camera capture to bitmap + context&#39; | 1.637 s | 0.0167 s | 0.0111 s |   7.32 KB |
