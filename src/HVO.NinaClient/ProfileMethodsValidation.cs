// Profile Methods Validation
// This file verifies that all profile methods are properly implemented and accessible

using HVO.NinaClient;
using HVO.NinaClient.Models;

namespace ProfileMethodsValidation;

public class ProfileMethodsValidator
{
    private readonly INinaApiClient _client;

    public ProfileMethodsValidator(INinaApiClient client)
    {
        _client = client;
    }

    public async Task ValidateAllProfileMethods()
    {
        // ShowProfileAsync - should be accessible with all parameter variations
        var activeProfile = await _client.ShowProfileAsync(active: true);
        var allProfiles = await _client.ShowProfileAsync(active: false);
        var defaultProfile = await _client.ShowProfileAsync();

        // ChangeProfileValueAsync - should accept string and object parameters
        var changeResult = await _client.ChangeProfileValueAsync("CameraSettings-PixelSize", 3.2);

        // SwitchProfileAsync - should accept string profile ID
        var switchResult = await _client.SwitchProfileAsync("test-profile-id");

        // GetProfileHorizonAsync - should return HorizonData
        var horizonResult = await _client.GetProfileHorizonAsync();
        if (horizonResult.IsSuccessful)
        {
            var horizon = horizonResult.Value;
            var altitudes = horizon.Altitudes;
            var azimuths = horizon.Azimuths;
        }
    }
}
