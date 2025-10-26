using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Models.Cameras;
using HVO.SkyMonitorV5.RPi.Models.Catalog;
using HVO.SkyMonitorV5.RPi.Tests.TestDrivers;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Integration;

[TestClass]
public sealed class ConfigurationApiIntegrationTests
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
    public async Task GetCameraDrivers_ReturnsDynamicDescriptors()
    {
        var response = await _client.GetAsync("api/v1.0/configuration/drivers").ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CameraDriverCatalogResponse>().ConfigureAwait(false);
        Assert.IsNotNull(payload, "Driver payload should be returned.");

        Assert.IsTrue(payload.Drivers.Any(driver => driver.Id == CameraDriverIdentifiers.SimulationMockMono),
            "Expected built-in mock mono driver to be present.");

        Assert.IsTrue(payload.Drivers.Any(driver => driver.Id == TestCameraDrivers.ConfigurableDriverId && driver.SupportsConfiguration),
            "Expected configurable test driver to surface with configuration metadata.");
    }

    [TestMethod]
    public async Task CreateCamera_WithTypedDriverSettings_PersistsCanonicalJson()
    {
        const string rawSettings = "{\"gain\":5,\"mode\":\"High\"}";
        var request = BuildCreateCameraRequest(TestCameraDrivers.ConfigurableDriverId, rawSettings);

        var response = await _client.PostAsJsonAsync("api/v1.0/configuration/equipment/cameras", request).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var catalog = await response.Content.ReadFromJsonAsync<EquipmentCatalogResponse>().ConfigureAwait(false);
        Assert.IsNotNull(catalog, "Equipment catalog should be returned after camera creation.");

        var created = catalog.Cameras.Single(camera => string.Equals(camera.Key, request.Key, StringComparison.Ordinal));
        Assert.AreEqual(request.DisplayName, created.DisplayName);
        Assert.AreEqual(TestCameraDrivers.ConfigurableDriverId, created.DriverId);
        Assert.AreEqual(CanonicalizeJson(rawSettings), created.DriverSettingsJson);
    }

    [TestMethod]
    public async Task CreateCamera_WithInvalidTypedSettings_ReturnsBadRequest()
    {
        var request = BuildCreateCameraRequest(TestCameraDrivers.ConfigurableDriverId, "{\"gain\":\"oops\"}");

        var response = await _client.PostAsJsonAsync("api/v1.0/configuration/equipment/cameras", request).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, "Invalid typed settings should yield a 400 response.");
    }

    private static CreateCameraRequest BuildCreateCameraRequest(string driverId, string driverSettingsJson)
    {
        var key = $"IntegrationCam{Guid.NewGuid():N}";

        return new CreateCameraRequest
        {
            Key = key,
            DisplayName = "Integration Test Camera",
            Manufacturer = "HVO",
            Model = "Test",
            DriverVersion = "1.0.0",
            AdapterName = "TestAdapter",
            DriverId = driverId,
            SyntheticProfile = "Integration",
            IsSynthetic = true,
            SensorWidthPixels = 1280,
            SensorHeightPixels = 720,
            PixelSizeMicrons = 4.8,
            SensorCxPixels = 640,
            SensorCyPixels = 360,
            ColorMode = "Color",
            SensorTechnology = "CMOS",
            BodyType = "Test",
            Cooling = "None",
            SupportsGainControl = true,
            SupportsExposureControl = true,
            SupportsTemperatureTelemetry = false,
            SupportsSoftwareBinning = true,
            AdditionalTags = new[] { "IntegrationTest" },
            DriverSettingsJson = driverSettingsJson
        };
    }

    private static string CanonicalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
