using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Models.System;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Integration;

[TestClass]
public sealed class SystemConfigToDiagnosticsIntegrationTests
{
  private SkyMonitorTestWebApplicationFactory _factory = null!;
  private HttpClient _client = null!;

  [TestInitialize]
  public async Task InitializeAsync()
  {
    _factory = new SkyMonitorTestWebApplicationFactory();
    await _factory.InitializeConfigurationStoreAsync().ConfigureAwait(false);
    _client = _factory.CreateClient();
  }

  [TestCleanup]
  public void Cleanup()
  {
    _client?.Dispose();
    _factory?.Dispose();
  }
}
