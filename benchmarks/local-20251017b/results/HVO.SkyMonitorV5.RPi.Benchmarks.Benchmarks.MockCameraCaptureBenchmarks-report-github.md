```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Unknown processor
.NET SDK 9.0.304
  [Host]           : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD
  EnvConfiguredJob : .NET 9.0.8 (9.0.825.36511), Arm64 RyuJIT AdvSIMD

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                    | Mean    | Error    | StdDev   | Allocated |
|------------------------------------------ |--------:|---------:|---------:|----------:|
| &#39;Mock camera capture to bitmap + context&#39; | 1.642 s | 0.0134 s | 0.0089 s |   8.16 KB |
