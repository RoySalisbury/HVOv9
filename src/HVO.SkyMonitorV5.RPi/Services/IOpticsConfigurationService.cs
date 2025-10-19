using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Models.Catalog;
using HVO.SkyMonitorV5.RPi.Models.Cameras;
using HVO.SkyMonitorV5.RPi.Models.Optics;
using HVO.SkyMonitorV5.RPi.Models.Rigs;

namespace HVO.SkyMonitorV5.RPi.Services;

public interface IOpticsConfigurationService
{
    Task<Result<EquipmentCatalogResponse>> GetCatalogAsync(CancellationToken cancellationToken);

    Task<Result<EquipmentCatalogResponse>> CreateRigAsync(CreateRigRequest request, CancellationToken cancellationToken);

    Task<Result<EquipmentCatalogResponse>> UpdateRigAsync(int rigId, UpdateRigRequest request, CancellationToken cancellationToken);

    Task<Result<EquipmentCatalogResponse>> DeleteRigAsync(int rigId, long? revision, CancellationToken cancellationToken);

    Task<Result<EquipmentCatalogResponse>> CreateCameraAsync(CreateCameraRequest request, CancellationToken cancellationToken);

    Task<Result<EquipmentCatalogResponse>> UpdateCameraAsync(int cameraId, UpdateCameraRequest request, CancellationToken cancellationToken);

    Task<Result<EquipmentCatalogResponse>> CreateOpticsAsync(CreateOpticsRequest request, CancellationToken cancellationToken);

    Task<Result<EquipmentCatalogResponse>> UpdateOpticsAsync(int opticsId, UpdateOpticsRequest request, CancellationToken cancellationToken);
}
