```

BenchmarkDotNet v0.13.11, Debian GNU/Linux 12 (bookworm) (container)
Intel Core i9-14900K, 1 CPU, 32 logical and 24 physical cores
.NET SDK 9.0.306
  [Host]           : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2
  EnvConfiguredJob : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2

Job=EnvConfiguredJob  IterationCount=10  WarmupCount=1  

```
| Method                                    | Mean    | Error    | StdDev   | Allocated |
|------------------------------------------ |--------:|---------:|---------:|----------:|
| &#39;Mock camera capture to bitmap + context&#39; | 1.592 s | 0.0096 s | 0.0063 s |   8.09 KB |
