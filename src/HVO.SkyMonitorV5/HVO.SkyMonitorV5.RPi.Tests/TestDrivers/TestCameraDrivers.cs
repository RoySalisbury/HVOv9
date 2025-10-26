using System;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Tests.TestDrivers;

/// <summary>
/// Shared test camera driver types used for registry and integration tests.
/// </summary>
internal static class TestCameraDrivers
{
    public const string ConfigurableDriverId = "Test.Configurable";
    public const string DuplicateDriverId = "Test.Duplicate";

    [CameraDriver(ConfigurableDriverId, DisplayName = "Configurable Test Driver", Description = "Test driver with typed configuration.", Version = "1.0.0", ConfigurationType = typeof(ConfigurableDriverSettings))]
    private sealed class ConfigurableTestCameraAdapter : TestCameraAdapterBase
    {
        public ConfigurableTestCameraAdapter(RigSpec rig)
            : base(rig)
        {
        }
    }

    internal sealed class ConfigurableDriverSettings
    {
        public int Gain { get; set; }

        public string? Mode { get; set; }
    }

    [CameraDriver(DuplicateDriverId, DisplayName = "Duplicate Test Driver A", Description = "Primary duplicate test driver.", Version = "1.0.0")]
    private sealed class DuplicateTestCameraAdapterA : TestCameraAdapterBase
    {
        public DuplicateTestCameraAdapterA(RigSpec rig)
            : base(rig)
        {
        }
    }

    [CameraDriver(DuplicateDriverId, DisplayName = "Duplicate Test Driver B", Description = "Secondary duplicate test driver that should be skipped.", Version = "1.0.0")]
    private sealed class DuplicateTestCameraAdapterB : TestCameraAdapterBase
    {
        public DuplicateTestCameraAdapterB(RigSpec rig)
            : base(rig)
        {
        }
    }

    private abstract class TestCameraAdapterBase : ICameraAdapter
    {
        protected TestCameraAdapterBase(RigSpec rig)
        {
            Rig = rig;
        }

        public RigSpec Rig { get; }

        public Task<Result<bool>> InitializeAsync(CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Success(true));

        public Task<Result<bool>> ShutdownAsync(CancellationToken cancellationToken)
            => Task.FromResult(Result<bool>.Success(true));

        public Task<Result<CapturedImage>> CaptureAsync(ExposureSettings exposure, CancellationToken cancellationToken)
            => Task.FromResult(Result<CapturedImage>.Failure(new NotSupportedException("Capture is not implemented for test drivers.")));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
