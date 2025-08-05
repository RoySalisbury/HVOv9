// This file validates that all Mount equipment methods are properly implemented
// and accessible through the INinaApiClient interface

using HVO.NinaClient;
using HVO.NinaClient.Models;
using HVO;

namespace ValidationExample;

public class MountMethodsValidation
{
    public async Task ValidateAllMountMethods(INinaApiClient client)
    {
        // Connection Management
        Result<MountInfo> infoResult = await client.GetMountInfoAsync();
        Result<IReadOnlyList<DeviceInfo>> devicesResult = await client.GetMountDevicesAsync();
        Result<IReadOnlyList<DeviceInfo>> rescanResult = await client.RescanMountDevicesAsync();
        Result<string> connectResult = await client.ConnectMountAsync("device123");
        Result<string> disconnectResult = await client.DisconnectMountAsync();

        // Mount Operations
        Result<string> homeResult = await client.HomeMountAsync();
        Result<string> trackingResult = await client.SetMountTrackingModeAsync(0); // Sidereal
        
        // Parking Operations
        Result<string> parkResult = await client.ParkMountAsync();
        Result<string> unparkResult = await client.UnparkMountAsync();
        Result<string> setParkResult = await client.SetMountParkPositionAsync();

        // Movement Operations
        Result<string> flipResult = await client.FlipMountAsync();
        Result<string> slewResult = await client.SlewMountAsync(
            ra: 10.68470833,
            dec: 41.26875,
            waitForResult: true,
            center: true,
            rotate: false,
            rotationAngle: 0.0);
        Result<string> stopSlewResult = await client.StopMountSlewAsync();

        // Synchronization
        Result<string> syncManualResult = await client.SyncMountAsync(ra: 15.0, dec: 45.0);
        Result<string> syncAutoResult = await client.SyncMountAsync(); // Auto platesolve
    }
}
