using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models.Optics;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Configuration;

public sealed partial class OpticsConfigurationTab : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    private IReadOnlyList<OpticsCatalogLens> _lenses = Array.Empty<OpticsCatalogLens>();
    private IReadOnlyList<OpticsRigSummary> _rigs = Array.Empty<OpticsRigSummary>();
    private IReadOnlyDictionary<string, IReadOnlyList<OpticsRigSummary>> _lensUsage = new Dictionary<string, IReadOnlyList<OpticsRigSummary>>(StringComparer.OrdinalIgnoreCase);

    private bool _isLoading;
    private string? _errorMessage;
    private string? _lastUpdatedMessage;
    private string? _activeRigKey;

    [Inject]
    public ILocalApiClient LocalApiClient { get; set; } = default!;

    [Inject]
    public ILogger<OpticsConfigurationTab>? Logger { get; set; }

    private CancellationToken CancellationToken => _lifetime.Token;

    private int LensCount => _lenses.Count;

    private int RigCount => _rigs.Count;

    private OpticsRigSummary? ActiveRig
    {
        get
        {
            if (_rigs.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(_activeRigKey))
            {
                var active = _rigs.FirstOrDefault(rig => string.Equals(rig.Key, _activeRigKey, StringComparison.OrdinalIgnoreCase));
                if (active is not null)
                {
                    return active;
                }
            }

            return _rigs.FirstOrDefault(rig => rig.IsActive) ?? _rigs.First();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadCatalogAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (!_lifetime.IsCancellationRequested)
        {
            _lifetime.Cancel();
        }

        _lifetime.Dispose();
    }

    private Task ReloadAsync()
        => LoadCatalogAsync();

    private async Task LoadCatalogAsync()
    {
        _isLoading = true;
        _errorMessage = null;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            var response = await LocalApiClient.GetOpticsCatalogAsync(CancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                _errorMessage = "Unable to load optics catalog from the local API.";
                return;
            }

            ApplyCatalog(response);
            _lastUpdatedMessage = $"Updated {DateTimeOffset.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException)
        {
            // component disposed
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load optics catalog data.");
            _errorMessage = ex.Message;
        }
        finally
        {
            _isLoading = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private void ApplyCatalog(OpticsCatalogResponse response)
    {
        _lenses = response.Lenses.OrderBy(lens => lens.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        _rigs = response.Rigs.OrderBy(rig => rig.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        _activeRigKey = response.ActiveRigKey;

        var usage = _rigs
            .GroupBy(rig => rig.LensKey ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<OpticsRigSummary>)group.ToList(),
                StringComparer.OrdinalIgnoreCase);

        _lensUsage = usage;
    }

    private IReadOnlyList<OpticsRigSummary> GetLensUsage(string lensKey)
    {
        if (_lensUsage.TryGetValue(lensKey, out var rigs))
        {
            return rigs;
        }

        return Array.Empty<OpticsRigSummary>();
    }

    private string GetRigUsageSummary(string lensKey)
    {
        var rigs = GetLensUsage(lensKey);
        if (rigs.Count == 0)
        {
            return "Not assigned to any rig.";
        }

        return string.Join(", ", rigs.Select(rig => rig.DisplayName));
    }

    private string FormatFocalLength(double value)
        => $"{value:F1} mm";

    private string FormatFieldOfView(double value)
        => $"{value:F1}°";

    private string FormatOptionalFieldOfView(double? value)
        => value.HasValue ? $"{value.Value:F1}°" : "--";

    private string FormatBoresight(OpticsRigSummary rig)
        => $"{rig.BoresightAltitudeDegrees:F1}° / {rig.BoresightAzimuthDegrees:F1}°";

    private async Task RequestRepaintAsync()
    {
        try
        {
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
