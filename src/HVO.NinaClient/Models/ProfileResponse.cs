using System.Collections.ObjectModel;

namespace HVO.NinaClient.Models;

/// <summary>
/// Response model for profile information from NINA API
/// Handles both active profile details and available profiles list scenarios
/// </summary>
[NamedOneOf("ActiveProfile", typeof(ProfileInfo), "AvailableProfiles", typeof(ReadOnlyCollection<ProfileInfo>))]
public partial class ProfileResponse;

