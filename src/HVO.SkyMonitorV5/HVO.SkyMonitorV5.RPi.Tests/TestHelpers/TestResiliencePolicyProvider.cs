using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Infrastructure.Resilience;
using Polly;

namespace HVO.SkyMonitorV5.RPi.Tests.TestHelpers;

internal sealed class TestResiliencePolicyProvider : IFrameExportResiliencePolicyProvider
{
    private readonly IAsyncPolicy _policy;

    public TestResiliencePolicyProvider(IAsyncPolicy? policy = null)
    {
        _policy = policy ?? Policy.NoOpAsync();
    }

    public IAsyncPolicy CreatePolicy() => _policy;
}
