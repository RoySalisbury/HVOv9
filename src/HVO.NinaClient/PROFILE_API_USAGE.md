# ShowProfileAsync Usage Examples

## Updated Method Signature

The `ShowProfileAsync` method has been updated to return a properly typed response instead of a generic `object`:

```csharp
public async Task<Result<ProfileResponse>> ShowProfileAsync(bool? active = null, CancellationToken cancellationToken = default)
```

## Usage Scenarios

### 1. Get Active Profile with Full Settings

```csharp
// Get the currently active profile with all detailed settings
var activeProfileResult = await ninaClient.ShowProfileAsync(active: true);

if (activeProfileResult.IsSuccessful && activeProfileResult.Value.ActiveProfile != null)
{
    var profile = activeProfileResult.Value.ActiveProfile;
    
    // Access profile information
    Console.WriteLine($"Active Profile: {profile.Name} (ID: {profile.Id})");
    Console.WriteLine($"Description: {profile.Description}");
    Console.WriteLine($"Last Used: {profile.LastUsed}");
    
    // Access camera settings
    if (profile.CameraSettings != null)
    {
        Console.WriteLine($"Camera Pixel Size: {profile.CameraSettings.PixelSize}");
        Console.WriteLine($"Camera Gain: {profile.CameraSettings.Gain}");
        Console.WriteLine($"Camera Offset: {profile.CameraSettings.Offset}");
    }
    
    // Access application settings
    if (profile.ApplicationSettings != null)
    {
        Console.WriteLine($"Sky Survey Source: {profile.ApplicationSettings.SkySurveySource}");
        Console.WriteLine($"Log Level: {profile.ApplicationSettings.LogLevel}");
    }
    
    // Use with ChangeProfileValueAsync - the path structure matches
    // For example, to change camera pixel size:
    await ninaClient.ChangeProfileValueAsync("CameraSettings-PixelSize", 5.6);
}
```

### 2. Get List of All Available Profiles

```csharp
// Get list of all profiles for selection/switching
var allProfilesResult = await ninaClient.ShowProfileAsync(active: false);

if (allProfilesResult.IsSuccessful && allProfilesResult.Value.AvailableProfiles != null)
{
    Console.WriteLine($"Currently Active Profile ID: {allProfilesResult.Value.ActiveProfileId}");
    Console.WriteLine("\nAvailable Profiles:");
    
    foreach (var profileSummary in allProfilesResult.Value.AvailableProfiles)
    {
        var activeIndicator = profileSummary.IsActive ? " (ACTIVE)" : "";
        Console.WriteLine($"- {profileSummary.Name} (ID: {profileSummary.Id}){activeIndicator}");
        Console.WriteLine($"  Description: {profileSummary.Description}");
        Console.WriteLine($"  Last Used: {profileSummary.LastUsed}");
    }
    
    // Switch to a different profile
    var profileToSwitchTo = allProfilesResult.Value.AvailableProfiles
        .FirstOrDefault(p => !p.IsActive);
        
    if (profileToSwitchTo != null)
    {
        var switchResult = await ninaClient.SwitchProfileAsync(profileToSwitchTo.Id);
        if (switchResult.IsSuccessful)
        {
            Console.WriteLine($"Switched to profile: {profileToSwitchTo.Name}");
        }
    }
}
```

### 3. Get Default Profile Information

```csharp
// Get default profile info (equivalent to active=true)
var defaultProfileResult = await ninaClient.ShowProfileAsync();

if (defaultProfileResult.IsSuccessful)
{
    // Handle both scenarios - the API might return either format
    if (defaultProfileResult.Value.ActiveProfile != null)
    {
        // Full profile details returned
        var profile = defaultProfileResult.Value.ActiveProfile;
        Console.WriteLine($"Active Profile: {profile.Name}");
    }
    else if (defaultProfileResult.Value.AvailableProfiles != null)
    {
        // List of profiles returned - find the active one
        var activeProfile = defaultProfileResult.Value.AvailableProfiles
            .FirstOrDefault(p => p.IsActive);
        Console.WriteLine($"Active Profile: {activeProfile?.Name}");
    }
}
```

### 4. Error Handling with Resilience

```csharp
var profileResult = await ninaClient.ShowProfileAsync(active: true);

if (!profileResult.IsSuccessful)
{
    // The method now benefits from automatic retry and circuit breaker logic
    switch (profileResult.Error)
    {
        case NinaConnectionException connEx:
            Console.WriteLine($"Connection issue: {connEx.Message}");
            // Network problems, timeouts - may have been retried automatically
            break;
            
        case NinaApiLogicalException logicalEx:
            Console.WriteLine($"API logical error: {logicalEx.Message}");
            // Business logic error from NINA
            break;
            
        case NinaApiHttpException httpEx:
            Console.WriteLine($"HTTP error {httpEx.StatusCode}: {httpEx.Message}");
            // HTTP status code issues
            break;
            
        default:
            Console.WriteLine($"Unexpected error: {profileResult.Error?.Message}");
            break;
    }
}
```

## Key Improvements

### ✅ Type Safety
- **Before**: `Result<object>` - no compile-time type checking
- **After**: `Result<ProfileResponse>` - full IntelliSense and type safety

### ✅ Structured Access
- **Before**: Casting and dynamic access to profile data
- **After**: Strongly-typed properties with proper JSON deserialization

### ✅ Consistent API Design
- **Before**: Generic object return that didn't match other methods
- **After**: Follows the same pattern as other response models (CameraInfoResponse, MountInfoResponse, etc.)

### ✅ Integration with Profile Management
- **Path Compatibility**: Profile structure matches the path format used in `ChangeProfileValueAsync`
- **Profile Switching**: Easy integration with `SwitchProfileAsync` using profile IDs

### ✅ Automatic Resilience
- **Retry Logic**: Network failures and timeouts are automatically retried
- **Circuit Breaker**: Participates in failure tracking to prevent cascading failures  
- **Consistent Error Types**: Uses the same NINA exception hierarchy as other methods

This update makes the profile management functionality much more robust and easier to use in client applications.
