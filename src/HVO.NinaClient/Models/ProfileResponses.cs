using System.Text.Json.Serialization;

namespace HVO.NinaClient.Models;

/// <summary>
/// Response wrapper for Profile information from NINA Advanced API
/// </summary>
public record ProfileInfoResponse : NinaApiResponse<ProfileInfo>;

/// <summary>
/// Response wrapper for Profile list from NINA Advanced API
/// </summary>
public record ProfileListResponse : NinaApiResponse<IReadOnlyList<ProfileInfo>>;

/// <summary>
/// Response wrapper for Horizon data from NINA Advanced API
/// </summary>
public record HorizonDataResponse : NinaApiResponse<HorizonData>;
