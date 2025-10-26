# HVO.TheSkyX - TheSkyX Automation Integration (PLANNED)

> **🚧 WORK IN PROGRESS - Planned Integration**
>
> This domain is planned for future development to integrate with **Software Bisque's TheSkyX Professional**, a comprehensive planetarium and telescope control software.

## 📦 Planned Domain Overview

The **HVO.TheSkyX** domain will enable:
- **TheSkyX TCP/IP automation** - Control telescope, camera, mount via TheSkyX JavaScript API
- **Planetarium integration** - Sky charts, object databases, ephemeris calculations
- **ASCOM device control** - Unified control of ASCOM-compatible equipment
- **Imaging automation** - FocusMax, AutoGuider, ACP Observatory Control
- **Sky6 compatibility** - Support for legacy TheSky6 installations

## 📁 Planned Projects

### HVO.TheSkyX.Client (Planned)
TCP/IP client library for TheSkyX automation:
- JavaScript command execution via TCP port 3040
- Telescope slewing and tracking
- Camera control and image acquisition
- Focus control and autofocus
- Mount park/unpark, calibration
- Sky chart manipulation

### HVO.TheSkyX.Models (Planned)
Shared models and DTOs:
- Celestial coordinates (RA/Dec, Alt/Az)
- Telescope capabilities and status
- Image metadata and FITS headers
- Target object information

## 🔑 Planned Features

### Telescope Control (Planned)

```csharp
// Future API design
public class TheSkyXTelescopeService
{
    private readonly TheSkyXTcpConnection _connection;
    
    public async Task<Result> SlewToCoordinatesAsync(
        double rightAscension,
        double declination)
    {
        var script = $@"
            sky6RASCOMTele.Connect();
            sky6RASCOMTele.SlewToRaDec({rightAscension}, {declination}, """");
        ";
        
        return await _connection.ExecuteScriptAsync(script);
    }
    
    public async Task<Result<TelescopeStatus>> GetStatusAsync()
    {
        var script = @"
            var ra = sky6RASCOMTele.dRa;
            var dec = sky6RASCOMTele.dDec;
            var isTracking = sky6RASCOMTele.IsTracking;
            
            Out = JSON.stringify({ ra: ra, dec: dec, tracking: isTracking });
        ";
        
        var result = await _connection.ExecuteScriptAsync(script);
        // Parse JSON result and return TelescopeStatus
    }
}
```

### Camera Integration (Planned)

```csharp
// Future API design
public class TheSkyXCameraService
{
    public async Task<Result<string>> TakeImageAsync(
        double exposureSeconds,
        ImageType imageType = ImageType.Light,
        string? filterName = null)
    {
        var script = $@"
            ccdsoftCamera.Connect();
            ccdsoftCamera.ExposureTime = {exposureSeconds};
            ccdsoftCamera.Frame = {(int)imageType};
            {(filterName != null ? $"ccdsoftCamera.FilterIndexZeroBased = GetFilterIndex('{filterName}');" : "")}
            
            ccdsoftCamera.TakeImage();
            
            // Wait for completion
            while (!ccdsoftCamera.IsExposureComplete) {{
                // polling loop
            }}
            
            var imagePath = ccdsoftCamera.LastImageFileName;
            Out = imagePath;
        ";
        
        return await _connection.ExecuteScriptAsync(script);
    }
}
```

## 🎓 Planned Usage Examples

### Automated Imaging Session

```csharp
// Future workflow
public class ImagingSessionService
{
    private readonly TheSkyXTelescopeService _telescope;
    private readonly TheSkyXCameraService _camera;
    private readonly RoofController _roof;
    
    public async Task<Result> RunImagingSessionAsync(
        List<ImagingTarget> targets,
        CancellationToken cancellationToken)
    {
        // 1. Open roof
        await _roof.OpenAsync();
        
        // 2. Connect and unpark telescope
        await _telescope.ConnectAsync();
        await _telescope.UnparkAsync();
        
        foreach (var target in targets)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            // 3. Slew to target
            await _telescope.SlewToCoordinatesAsync(target.RA, target.Dec);
            
            // 4. Autofocus
            await _telescope.AutofocusAsync();
            
            // 5. Take images
            for (int i = 0; i < target.ExposureCount; i++)
            {
                await _camera.TakeImageAsync(
                    exposureSeconds: target.ExposureSeconds,
                    imageType: ImageType.Light,
                    filterName: target.Filter);
            }
        }
        
        // 6. Park telescope
        await _telescope.ParkAsync();
        
        // 7. Close roof
        await _roof.CloseAsync();
        
        return Result.Success();
    }
}
```

## 🔗 Planned Dependencies

- `HVO` - Core library (Result<T>, Option<T>)
- `System.Net.Sockets` - TCP/IP communication
- `Microsoft.Extensions.Logging.Abstractions` - Structured logging
- TheSkyX Professional (external software requirement)

## 📚 Intended For

- Automated deep-sky imaging workflows
- Remote observatory control
- Observatory integration testing
- Legacy TheSky6 automation (if needed)

## 🎨 Integration Considerations

### Why TheSkyX?
- **Industry standard** - Widely used in amateur observatories
- **ASCOM hub** - Unified device control
- **Robust automation** - JavaScript API with decades of refinement
- **Planetarium features** - Rich sky database, ephemeris calculations

### Alternative: NINA
For users without TheSkyX, see [HVO.NINA](../HVO.NINA/README.md) which provides similar automation capabilities with the free, open-source NINA software.

### TCP/IP Protocol
TheSkyX automation via TCP socket on port 3040:
```
Client → TheSkyX:  JavaScript command
TheSkyX → Client:  Result or error
```

## ⚠️ Current Status

### Existing Files
- `Implementation/TheSkyXTcpConnection.cs` - Empty placeholder
- `Implementation/TheSkyXCameraService.cs` - Empty placeholder
- `Extensions/ServiceCollectionExtensions.cs` - Placeholder

### Development Needed
- [ ] Create HVO.TheSkyX.Client project
- [ ] Implement TCP/IP connection wrapper
- [ ] Add JavaScript command builder
- [ ] Implement telescope control APIs
- [ ] Add camera control APIs
- [ ] Create unit tests with TheSkyX simulator
- [ ] Add configuration management
- [ ] Document setup and installation
- [ ] Create integration examples

## 🔄 Roadmap

### Phase 1: Foundation (Q1 2026)
- [ ] TCP/IP connection manager
- [ ] JavaScript command execution
- [ ] Basic telescope slewing
- [ ] Camera exposure control

### Phase 2: Advanced Control (Q2 2026)
- [ ] Autofocus integration
- [ ] Filter wheel control
- [ ] Mount calibration
- [ ] Image download and FITS conversion

### Phase 3: Automation (Q3 2026)
- [ ] Target scheduling
- [ ] Meridian flip handling
- [ ] Weather integration
- [ ] Error recovery

### Phase 4: Observatory Integration (Q4 2026)
- [ ] Roof controller coordination
- [ ] NINA interoperability
- [ ] Multi-night scheduling
- [ ] Performance optimization

## 📖 Related Documentation

- [Software Bisque TheSkyX](https://www.bisque.com/product/theskyx/)
- [TheSkyX JavaScript API Documentation](https://www.bisque.com/help/TheSkyX/TheSkyX.htm)
- [HVO.NINA - Alternative Automation](../HVO.NINA/README.md)
- [Observatory Automation Architecture](../../docs/observatory-automation.md) *(planned)*

## 💡 Contributing

This domain is in the planning phase. If you're interested in TheSkyX integration:
1. Review existing TheSkyX automation scripts
2. Study the TCP/IP protocol documentation
3. Propose API designs via GitHub issues
4. Share use cases and requirements

**Contact**: Open an issue with tag `enhancement:theskyx` to discuss implementation priorities.