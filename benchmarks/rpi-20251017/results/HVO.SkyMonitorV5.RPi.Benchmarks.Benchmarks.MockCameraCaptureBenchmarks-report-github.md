```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.306
  [Host]           : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.10 (9.0.1025.47515), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                    | Mean    | Error    | StdDev   | Allocated |
|------------------------------------------ |--------:|---------:|---------:|----------:|
| &#39;Mock camera capture to bitmap + context&#39; | 1.698 s | 0.0190 s | 0.0113 s |   7.93 KB |
