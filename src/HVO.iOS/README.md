# HVO.iOS - iPad and Mobile Applications

[![iOS (iPad) CI](https://github.com/RoySalisbury/HVOv9/actions/workflows/ios.yml/badge.svg?branch=main)](https://github.com/RoySalisbury/HVOv9/actions/workflows/ios.yml)

Domain containing .NET MAUI applications for iOS/iPadOS, providing native mobile interfaces for observatory control and monitoring.

## 📦 Domain Overview

The **HVO.iOS** domain delivers:
- **Native iPad apps** - Touch-optimized interfaces for observatory equipment
- **.NET MAUI framework** - Cross-platform .NET for iOS/Android/macOS
- **Local network control** - Direct connection to observatory systems via WiFi
- **Offline capability** - Works without internet (local network only)
- **Dark mode optimized** - Preserves night vision for field use
- **macOS development** - Requires Mac for building and deployment

## 📁 Projects in This Domain

### HVO.RoofControllerV4.iPad
Native iPad app for roof control:
- Large touch-friendly buttons for roof open/close
- Real-time limit switch status display
- Emergency stop button
- Safety timer visualization
- Dead-man timer progress indicator
- Network autodiscovery via mDNS/Bonjour
- Designed for 12.9" iPad Pro (also works on smaller iPads)

## 🔑 Key Features

### Touch-Optimized Roof Control

```xml
<!-- MAUI XAML UI -->
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="HVO.RoofControllerV4.iPad.MainPage"
             BackgroundColor="#05070d">
    
    <Grid RowDefinitions="Auto,*,Auto" Padding="20">
        
        <!-- Status Header -->
        <VerticalStackLayout Grid.Row="0" Spacing="10">
            <Label Text="{Binding RoofStatus}" 
                   FontSize="32" 
                   FontAttributes="Bold"
                   TextColor="#f8fafc"
                   HorizontalOptions="Center"/>
            
            <HorizontalStackLayout Spacing="20" HorizontalOptions="Center">
                <Label Text="Closed Limit:" TextColor="#94a3b8"/>
                <Label Text="{Binding ClosedLimitStatus}" 
                       TextColor="{Binding ClosedLimitColor}"/>
                
                <Label Text="Open Limit:" TextColor="#94a3b8"/>
                <Label Text="{Binding OpenLimitStatus}" 
                       TextColor="{Binding OpenLimitColor}"/>
            </HorizontalStackLayout>
        </VerticalStackLayout>
        
        <!-- Control Buttons -->
        <VerticalStackLayout Grid.Row="1" 
                            Spacing="30" 
                            VerticalOptions="Center"
                            HorizontalOptions="Center">
            
            <Button Text="Open Roof"
                    Command="{Binding OpenRoofCommand}"
                    IsEnabled="{Binding CanOpen}"
                    BackgroundColor="#22c55e"
                    TextColor="White"
                    FontSize="24"
                    FontAttributes="Bold"
                    WidthRequest="300"
                    HeightRequest="100"
                    CornerRadius="12"/>
            
            <Button Text="Close Roof"
                    Command="{Binding CloseRoofCommand}"
                    IsEnabled="{Binding CanClose}"
                    BackgroundColor="#3b82f6"
                    TextColor="White"
                    FontSize="24"
                    FontAttributes="Bold"
                    WidthRequest="300"
                    HeightRequest="100"
                    CornerRadius="12"/>
            
            <Button Text="EMERGENCY STOP"
                    Command="{Binding EmergencyStopCommand}"
                    BackgroundColor="#ef4444"
                    TextColor="White"
                    FontSize="24"
                    FontAttributes="Bold"
                    WidthRequest="300"
                    HeightRequest="100"
                    CornerRadius="12"/>
        </VerticalStackLayout>
        
        <!-- Timer Display -->
        <VerticalStackLayout Grid.Row="2" 
                            Spacing="5"
                            IsVisible="{Binding IsMoving}">
            <Label Text="Dead-Man Timer" 
                   TextColor="#94a3b8" 
                   HorizontalOptions="Center"/>
            <ProgressBar Progress="{Binding TimerProgress}" 
                        ProgressColor="#3b82f6"/>
            <Label Text="{Binding TimerSecondsRemaining, StringFormat='{0} seconds'}" 
                   TextColor="#f8fafc" 
                   HorizontalOptions="Center"
                   FontSize="18"/>
        </VerticalStackLayout>
        
    </Grid>
</ContentPage>
```

### MVVM ViewModel Pattern

```csharp
public class RoofControlViewModel : ObservableObject
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RoofControlViewModel> _logger;
    private Timer? _statusTimer;
    
    public RoofControlViewModel(ILogger<RoofControlViewModel> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        
        OpenRoofCommand = new AsyncRelayCommand(OpenRoofAsync, () => CanOpen);
        CloseRoofCommand = new AsyncRelayCommand(CloseRoofAsync, () => CanClose);
        EmergencyStopCommand = new RelayCommand(EmergencyStop);
        
        // Poll roof status every 500ms
        _statusTimer = new Timer(async _ => await UpdateStatusAsync(), null, 
            TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
    }
    
    [ObservableProperty]
    private string _roofStatus = "Unknown";
    
    [ObservableProperty]
    private bool _closedLimitActive;
    
    [ObservableProperty]
    private bool _openLimitActive;
    
    [ObservableProperty]
    private bool _isMoving;
    
    [ObservableProperty]
    private int _timerSecondsRemaining;
    
    public bool CanOpen => RoofStatus == "Closed" && !IsMoving;
    public bool CanClose => RoofStatus == "Open" && !IsMoving;
    
    public string ClosedLimitStatus => ClosedLimitActive ? "ACTIVE" : "Inactive";
    public string OpenLimitStatus => OpenLimitActive ? "ACTIVE" : "Inactive";
    
    public Color ClosedLimitColor => ClosedLimitActive 
        ? Color.FromArgb("#22c55e") 
        : Color.FromArgb("#64748b");
    
    public double TimerProgress => TimerSecondsRemaining / 30.0;
    
    private async Task UpdateStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<RoofStatusResponse>(
                "http://roofcontroller.local/api/v1/roof/status");
            
            if (response != null)
            {
                RoofStatus = response.State;
                ClosedLimitActive = response.ClosedLimitActive;
                OpenLimitActive = response.OpenLimitActive;
                IsMoving = response.IsMoving;
                TimerSecondsRemaining = response.TimerRemaining;
                
                // Update command can-execute state
                OnPropertyChanged(nameof(CanOpen));
                OnPropertyChanged(nameof(CanClose));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update roof status");
        }
    }
    
    private async Task OpenRoofAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync(
                "http://roofcontroller.local/api/v1/roof/open", null);
            response.EnsureSuccessStatusCode();
            
            await App.Current!.MainPage!.DisplayAlert(
                "Success", 
                "Roof opening...", 
                "OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open roof");
            await App.Current!.MainPage!.DisplayAlert(
                "Error", 
                $"Failed to open roof: {ex.Message}", 
                "OK");
        }
    }
    
    private async Task CloseRoofAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync(
                "http://roofcontroller.local/api/v1/roof/close", null);
            response.EnsureSuccessStatusCode();
            
            await App.Current!.MainPage!.DisplayAlert(
                "Success", 
                "Roof closing...", 
                "OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close roof");
            await App.Current!.MainPage!.DisplayAlert(
                "Error", 
                $"Failed to close roof: {ex.Message}", 
                "OK");
        }
    }
    
    private void EmergencyStop()
    {
        try
        {
            var response = _httpClient.PostAsync(
                "http://roofcontroller.local/api/v1/roof/stop", null)
                .GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Emergency stop failed");
        }
    }
}
```

### Network Discovery

```csharp
public class NetworkDiscoveryService
{
    public async Task<Option<string>> DiscoverRoofControllerAsync()
    {
        try
        {
            // Use mDNS/Bonjour to find roofcontroller.local
            var resolver = new ServiceResolver();
            var service = await resolver.ResolveServiceAsync("_http._tcp", "roofcontroller");
            
            if (service != null)
            {
                var baseUrl = $"http://{service.HostName}:{service.Port}";
                return Option<string>.Some(baseUrl);
            }
            
            return Option<string>.None();
        }
        catch
        {
            // Fallback to default
            return Option<string>.Some("http://roofcontroller.local");
        }
    }
}
```

## 🎓 Usage Examples

### Deployment to iPad

```bash
# From workspace root
cd src/HVO.iOS

# Build for iPad Simulator (macOS only)
./scripts/run-roofcontroller-ipad-sim.sh --configuration Release

# Build for physical iPad device (requires provisioning profile)
./scripts/run-roofcontroller-ipad-device.sh --configuration Release
```

### Configuration Management

```csharp
// MauiProgram.cs
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder
        .UseMauiApp<App>()
        .ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });
    
    // Register services
    builder.Services.AddSingleton<ILogger>(
        LoggerFactory.Create(b => b.AddDebug()).CreateLogger("HVO"));
    builder.Services.AddSingleton<NetworkDiscoveryService>();
    builder.Services.AddTransient<RoofControlViewModel>();
    
    // Configure HTTP client
    builder.Services.AddHttpClient("RoofController", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });
    
    return builder.Build();
}
```

## 🧪 Testing

### Run on iOS Simulator (macOS only)
```bash
dotnet build -f net9.0-ios -c Debug
dotnet run -f net9.0-ios --no-build
```

### Deploy to Physical iPad
```bash
# Requires:
# - Xcode installed
# - Apple Developer account
# - Provisioning profile configured
# - iPad connected via USB or WiFi

export HVO_ROOF_IPAD_DEVICE_UDID="00008030-XXXXXXXXXXXX"
./scripts/run-roofcontroller-ipad-device.sh --configuration Release
```

### Build Domain Solution
```bash
cd src/HVO.iOS
dotnet build HVO.iOS.sln
```

## ⚙️ Development Requirements

### macOS Requirements
- **macOS** 13.0 (Ventura) or later
- **Xcode** 15.0 or later
- **.NET 9 SDK** with iOS workload
- **Apple Developer Account** (for device deployment)

### Install iOS Workload
```bash
sudo dotnet workload install ios
```

### Verify Installation
```bash
dotnet workload list
# Should show: ios [9.0.xxx/9.0.100]
```

## 📱 UI Design Guidelines

### Touch Targets
- **Minimum size**: 44×44 pt (Apple HIG)
- **Preferred size**: 88×88 pt for critical controls
- **Spacing**: 20pt between controls

### Typography
- **Headers**: 32pt Bold
- **Body**: 18pt Regular
- **Small text**: 14pt Regular

### Color Palette (HVO Dark for iPad)
```csharp
public static class AppColors
{
    public static Color Background = Color.FromArgb("#05070d");
    public static Color Foreground = Color.FromArgb("#f8fafc");
    public static Color Primary = Color.FromArgb("#3b82f6");
    public static Color Success = Color.FromArgb("#22c55e");
    public static Color Danger = Color.FromArgb("#ef4444");
    public static Color Muted = Color.FromArgb("#94a3b8");
}
```

## 🔗 Dependencies

- `.NET MAUI` - Cross-platform UI framework
- `CommunityToolkit.Mvvm` - MVVM helpers
- `Microsoft.Extensions.Logging` - Logging abstractions
- Backend: `HVO.RoofControllerV4.RPi` API

## 📚 Target Devices

### Tested On
- iPad Pro 12.9" (5th gen) - Primary target
- iPad Pro 11" (4th gen)
- iPad Air (5th gen)
- iPad (10th gen)

### iOS Versions
- iOS 17.0+ required
- Optimized for iOS 18.0

## 🎨 Design Decisions

### Why MAUI Over Native Swift?
- **Code sharing** - Share logic with Blazor/ASP.NET Core
- **Rapid development** - Single codebase for iOS/Android/macOS
- **C# ecosystem** - Leverage existing .NET libraries
- **Maintenance** - One team, one language

### Why iPad-Specific?
- **Observatory setting** - Dedicated device at telescope site
- **Screen size** - Large controls for gloved hands or nighttime use
- **Always available** - Mounted in observatory, always charged
- **Network** - Stable local WiFi connection

### Offline-First Design
- **No cloud dependency** - Works without internet
- **Local network** - Direct HTTP to Raspberry Pi controllers
- **Fast response** - No round-trip to cloud servers
- **Privacy** - Observatory data stays on-site

## 🔄 Future Enhancements

- [ ] Add weather dashboard page
- [ ] Integrate sky monitor live view
- [ ] Add NINA equipment status page
- [ ] Support multiple observatory profiles
- [ ] Add haptic feedback for button presses
- [ ] Implement Siri shortcuts ("Hey Siri, open the roof")
- [ ] Create Apple Watch complication
- [ ] Add push notifications for weather alerts
- [ ] Support landscape orientation
- [ ] Create Android version

## 📖 Related Documentation

- [.NET MAUI Documentation](https://learn.microsoft.com/en-us/dotnet/maui/)
- [Apple Human Interface Guidelines](https://developer.apple.com/design/human-interface-guidelines/)
- [HVO.RoofControllerV4 API](../HVO.RoofControllerV4/README.md)
- [iPad Deployment Scripts](scripts/README.md) *(if exists)*

## 💡 Contributing

iPad development requires macOS. To contribute:
1. Install Xcode and .NET iOS workload
2. Use iPad Simulator for initial testing
3. Request TestFlight access for device testing
4. Follow Apple HIG for UI/UX
5. Test with gloves (nighttime observatory use)

**Design Priority**: Large, easy-to-tap controls for use in darkness with gloved hands.
