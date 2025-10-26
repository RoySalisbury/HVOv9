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
using LegacyFitsBitDepth = HVO.SkyMonitorV5.RPi.Options.FitsBitDepth;
using LegacyFitsCompression = HVO.SkyMonitorV5.RPi.Options.FitsCompressionKind;

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

  [TestMethod]
  public async Task UpdateFitsExport_ViaSystemApi_IsReflectedInDiagnostics()
  {
    // Arrange: toggle processed FITS on, raw off, with specific options
    var update = new UpdateSystemFitsExportRequest
    {
      Revision = 0,
      EnableForRaw = false,
      EnableForProcessed = true,
      BitDepth = LegacyFitsBitDepth.U16,
      UnsignedU16 = true,
      Compression = FitsCompressionKind.Rice,
      WriteChecksum = true
    };

    var put = await _client.PutAsJsonAsync("api/v1.0/configuration/system/fits-export", update, new JsonSerializerOptions(JsonSerializerDefaults.Web)).ConfigureAwait(false);
    if (!put.IsSuccessStatusCode)
    {
      var body = await put.Content.ReadAsStringAsync().ConfigureAwait(false);
      if (body.Contains("unable to open database file", StringComparison.OrdinalIgnoreCase))
      {
        Assert.Inconclusive("System API integration skipped: SQLite not available in environment.");
      }
      Assert.Fail($"PUT fits-export failed {(int)put.StatusCode} {put.StatusCode}. Body: {body}");
    }

    // Act: call diagnostics for effective frame-export view
    var action = await _client.GetAsync("api/v1.0/diagnostics/frame-export").ConfigureAwait(false);
    if (!action.IsSuccessStatusCode)
    {
      var errorBody = await action.Content.ReadAsStringAsync().ConfigureAwait(false);
      if (errorBody.Contains("unable to open database file", StringComparison.OrdinalIgnoreCase))
      {
        Assert.Inconclusive("Diagnostics integration skipped: SQLite not available in environment.");
      }
      Assert.Fail($"Diagnostics endpoint returned {(int)action.StatusCode} {action.StatusCode}. Body: {errorBody}");
    }
    var payload = await action.Content.ReadFromJsonAsync<DiagnosticsFrameExportResponse>().ConfigureAwait(false);

    // Assert: processed archive reflects FITS; raw remains default unless configured elsewhere
    Assert.IsNotNull(payload);
    Assert.IsNotNull(payload!.Processed.Archive);
    Assert.AreEqual("Fits", payload.Processed.Archive.Format);
    Assert.AreEqual("image/fits", payload.Processed.Archive.ContentType);
    Assert.AreEqual("fits", payload.Processed.Archive.FileExtension);
    Assert.IsFalse(payload.Processed.Archive.IsRaster);
    Assert.IsNotNull(payload.Processed.Archive.Fits);
    StringAssert.Contains(payload.Processed.Archive.Fits!.Compression, "Rice");

    // Delivery should remain raster (publisher enforces at runtime), diagnostics only reports the configured encodings
    // and is not aware of the runtime raster enforcement; thus Delivery may be null in diagnostics if not set.
  }
}
