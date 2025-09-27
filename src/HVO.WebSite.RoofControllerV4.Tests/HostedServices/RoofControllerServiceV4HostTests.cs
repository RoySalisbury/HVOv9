using System.Collections.Concurrent;
using FluentAssertions;
using HVO;
using HVO.WebSite.RoofControllerV4.HostedServices;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HVO.WebSite.RoofControllerV4.Tests.HostedServices;

[TestClass]
public sealed class RoofControllerServiceV4HostTests
{
    [TestMethod]
    public async Task ExecuteAsync_ShouldInitializeAndStopOnCancellation()
    {
        // Arrange
        var service = new FakeRoofControllerServiceV4();
        var host = new TestableRoofControllerServiceV4Host(service);
        using var cts = new CancellationTokenSource();

        // Act
        var executeTask = host.ExecuteForTestAsync(cts.Token);
        await WaitUntilAsync(() => service.InitializationCallCount >= 1, TimeSpan.FromSeconds(1));

        service.IsInitializationSuccessful.Should().BeTrue();
        service.IsInitialized.Should().BeTrue();

        cts.Cancel();
        await executeTask.WaitAsync(TimeSpan.FromSeconds(1));

        // Assert
        service.InitializationCallCount.Should().Be(1);
        service.StopCallCount.Should().Be(1);
        service.LastStopReason.Should().Be(RoofControllerStopReason.SystemDisposal);
    }

    [TestMethod]
    public async Task ExecuteAsync_ShouldRetryInitializationUntilSuccessful()
    {
        // Arrange
        var service = new FakeRoofControllerServiceV4();
        service.EnqueueInitialization(Result<bool>.Failure(new InvalidOperationException("init failed")), markInitialized: false);
        service.EnqueueInitialization(Result<bool>.Success(true), markInitialized: true);

        var host = new TestableRoofControllerServiceV4Host(service);
        using var cts = new CancellationTokenSource();

        // Act
        var executeTask = host.ExecuteForTestAsync(cts.Token);
        await WaitUntilAsync(() => service.InitializationCallCount >= 2, TimeSpan.FromSeconds(1));

        service.IsInitialized.Should().BeTrue();

        cts.Cancel();
        await executeTask.WaitAsync(TimeSpan.FromSeconds(1));

        // Assert
        service.InitializationCallCount.Should().Be(2);
        service.StopCallCount.Should().Be(1);
        service.LastStopReason.Should().Be(RoofControllerStopReason.SystemDisposal);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within the allotted time.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class TestableRoofControllerServiceV4Host : RoofControllerServiceV4Host
    {
        public TestableRoofControllerServiceV4Host(IRoofControllerServiceV4 roofControllerService)
            : base(NullLogger<RoofControllerServiceV4Host>.Instance, Options.Create(new RoofControllerHostOptionsV4
            {
                RestartOnFailureWaitTime = 0
            }), roofControllerService)
        {
        }

        public Task ExecuteForTestAsync(CancellationToken token) => ExecuteAsync(token);
    }

    private sealed class FakeRoofControllerServiceV4 : IRoofControllerServiceV4
    {
        private readonly ConcurrentQueue<(Result<bool> Result, bool MarkInitialized)> _initializationResults = new();
        private int _initializationCallCount;

        public event EventHandler<RoofStatusChangedEventArgs>? StatusChanged;

        public int InitializationCallCount => Volatile.Read(ref _initializationCallCount);
        public bool IsInitializationSuccessful { get; private set; }
        public bool IsInitialized { get; private set; }
    public int StopCallCount { get; private set; }
    public RoofControllerStopReason LastStopReason { get; private set; } = RoofControllerStopReason.None;

    public RoofControllerStatus Status { get; set; } = RoofControllerStatus.Closed;
    public bool IsMoving { get; set; }
    public DateTimeOffset? LastTransitionUtc { get; set; }
    public bool IsWatchdogActive { get; set; }
    public double? WatchdogSecondsRemaining { get; set; }
    public bool IsAtSpeed { get; set; }
    public bool IsServiceDisposed { get; set; }

        public void EnqueueInitialization(Result<bool> result, bool markInitialized) => _initializationResults.Enqueue((result, markInitialized));

    public RoofStatusResponse GetCurrentStatusSnapshot() => new(Status, IsMoving, LastStopReason, LastTransitionUtc, IsWatchdogActive, WatchdogSecondsRemaining, IsAtSpeed);

        public Task<Result<bool>> Initialize(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _initializationCallCount);

            if (!_initializationResults.TryDequeue(out var entry))
            {
                entry = (Result<bool>.Success(true), true);
            }

            var result = entry.Result;
            if (result.IsSuccessful && entry.MarkInitialized)
            {
                IsInitialized = true;
                IsInitializationSuccessful = true;
            }
            else if (!result.IsSuccessful)
            {
                IsInitialized = false;
            }

            return Task.FromResult(result);
        }

        public Result<RoofControllerStatus> Stop(RoofControllerStopReason reason = RoofControllerStopReason.NormalStop)
        {
            StopCallCount++;
            LastStopReason = reason;
            Status = RoofControllerStatus.Stopped;
            return Result<RoofControllerStatus>.Success(Status);
        }

        public Result<RoofControllerStatus> Open() => Result<RoofControllerStatus>.Success(RoofControllerStatus.Open);

        public Result<RoofControllerStatus> Close() => Result<RoofControllerStatus>.Success(RoofControllerStatus.Closed);

        public void RefreshStatus(bool forceHardwareRead = false)
        {
            StatusChanged?.Invoke(this, new RoofStatusChangedEventArgs(GetCurrentStatusSnapshot()));
        }

        public Task<Result<bool>> ClearFault(int pulseMs = 250, CancellationToken cancellationToken = default) => Task.FromResult(Result<bool>.Success(true));
    }
}
