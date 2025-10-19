using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Models.Optics;

namespace HVO.SkyMonitorV5.RPi.Services;

public interface IOpticsConfigurationService
{
    Task<Result<OpticsCatalogResponse>> GetCatalogAsync(CancellationToken cancellationToken);

    Task<Result<OpticsCatalogResponse>> CreateRigAsync(CreateOpticsRigRequest request, CancellationToken cancellationToken);

    Task<Result<OpticsCatalogResponse>> UpdateRigAsync(int rigId, UpdateOpticsRigRequest request, CancellationToken cancellationToken);

    Task<Result<OpticsCatalogResponse>> DeleteRigAsync(int rigId, long? revision, CancellationToken cancellationToken);
}
