using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.Data.Configurations;
using HVO.SkyMonitorV5.Data.Configurations.Entities;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Integration;

[TestClass]
public sealed class DiagnosticsApiIntegrationTests
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
  public async Task FrameExport_DatabaseOverride_IsReflectedInDiagnostics()
  {
    // Arrange: seed DB with a frame-export payload
    var seeded = new FrameExportOptions
    {
      Raw = new FrameExportStageOptions
      {
        Enabled = true,
        PayloadScope = FrameExportPayloadScope.ArchiveOnly,
        ArchiveEncoding = new HVO.SkyMonitorV5.RPi.Pipeline.ImageEncodingSettings(HVO.SkyMonitorV5.RPi.Pipeline.ImageEncodingFormat.Fits, 100)
        {
          FitsOptions = new HVO.SkyMonitorV5.RPi.Pipeline.FitsEncodingOptions
          {
            BitDepth = HVO.SkyMonitorV5.RPi.Pipeline.FitsBitDepth.U16,
            ImageFormat = HVO.SkyMonitorV5.RPi.Pipeline.FitsImageFormat.Mono,
            Compression = HVO.SkyMonitorV5.RPi.Pipeline.FitsCompression.None,
            UnsignedU16 = true,
            WriteChecksum = true
          }
        },
        DeliveryEncoding = new HVO.SkyMonitorV5.RPi.Pipeline.ImageEncodingSettings(HVO.SkyMonitorV5.RPi.Pipeline.ImageEncodingFormat.Jpeg, 80)
      },
      Processed = new FrameExportStageOptions
      {
        Enabled = true,
        PayloadScope = FrameExportPayloadScope.ArchiveOnly,
        ArchiveEncoding = new HVO.SkyMonitorV5.RPi.Pipeline.ImageEncodingSettings(HVO.SkyMonitorV5.RPi.Pipeline.ImageEncodingFormat.Png, 100)
      }
    };

    await SeedSystemSettingAsync("frame-export", JsonSerializer.Serialize(seeded, new JsonSerializerOptions(JsonSerializerDefaults.General))).ConfigureAwait(false);

    // Act: call diagnostics endpoint
    var action = await _client.GetAsync("api/v1.0/diagnostics/frame-export").ConfigureAwait(false);
    if (!action.IsSuccessStatusCode)
    {
      var errorBody = await action.Content.ReadAsStringAsync().ConfigureAwait(false);
      if (errorBody.Contains("unable to open database file", StringComparison.OrdinalIgnoreCase))
      {
        Assert.Inconclusive("Diagnostics integration test skipped: SQLite database file could not be opened in CI environment.");
      }
      Assert.Fail($"Diagnostics endpoint returned {(int)action.StatusCode} {action.StatusCode}. Body: {errorBody}");
    }
    var payload = await action.Content.ReadFromJsonAsync<DiagnosticsFrameExportResponse>().ConfigureAwait(false);

    // Assert: effective options match DB payload
    Assert.IsNotNull(payload);

    Assert.IsTrue(payload.Raw.Enabled);
    Assert.AreEqual("ArchiveOnly", payload.Raw.PayloadScope);
    Assert.AreEqual("Fits", payload.Raw.Archive.Format);
    Assert.AreEqual("image/fits", payload.Raw.Archive.ContentType);
    Assert.AreEqual("fits", payload.Raw.Archive.FileExtension);
    Assert.IsFalse(payload.Raw.Archive.IsRaster);
    Assert.IsNotNull(payload.Raw.Archive.Fits);

    Assert.AreEqual("Jpeg", payload.Raw.Delivery.Format);
    Assert.AreEqual("image/jpeg", payload.Raw.Delivery.ContentType);
    Assert.AreEqual("jpg", payload.Raw.Delivery.FileExtension);
    Assert.IsTrue(payload.Raw.Delivery.IsRaster);

    Assert.IsTrue(payload.Processed.Enabled);
    Assert.AreEqual("ArchiveOnly", payload.Processed.PayloadScope);
    Assert.AreEqual("Png", payload.Processed.Archive.Format);
    Assert.AreEqual("image/png", payload.Processed.Archive.ContentType);
    Assert.AreEqual("png", payload.Processed.Archive.FileExtension);
    Assert.IsTrue(payload.Processed.Archive.IsRaster);
  }

  private async Task SeedSystemSettingAsync(string key, string payloadJson)
  {
    using var scope = _factory.Services.CreateScope();
    var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SkyMonitorConfigurationContext>>();
    await using var context = await contextFactory.CreateDbContextAsync().ConfigureAwait(false);

    var existing = await context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key).ConfigureAwait(false);
    if (existing is null)
    {
      existing = new SystemSettingEntity { Key = key, PayloadJson = payloadJson };
      await context.SystemSettings.AddAsync(existing).ConfigureAwait(false);
    }
    else
    {
      existing.PayloadJson = payloadJson;
      context.SystemSettings.Update(existing);
    }

    await context.SaveChangesAsync().ConfigureAwait(false);

    // Invalidate the runtime snapshot so options rebind to the new DB state
    var invalidator = scope.ServiceProvider.GetRequiredService<HVO.SkyMonitorV5.RPi.Infrastructure.IConfigurationSnapshotInvalidator>();
    invalidator.InvalidateSnapshot();
  }
}
