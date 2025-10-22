using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models.ImageHistory;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Pages;

public sealed partial class ImageHistoryFramePreview : ComponentBase
{
    private ImageHistoryFrameDetailViewModel? _viewModel;
    private bool _isLoading;
    private string? _errorMessage;

    [Parameter]
    public Guid FrameId { get; set; }

    [Inject]
    private IImageHistoryService ImageHistoryService { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ILogger<ImageHistoryFramePreview> Logger { get; set; } = default!;

    protected override async Task OnParametersSetAsync()
    {
        await LoadFrameAsync().ConfigureAwait(false);
    }

    private async Task LoadFrameAsync()
    {
        _isLoading = true;
        _errorMessage = null;
        _viewModel = null;
        await InvokeAsync(StateHasChanged).ConfigureAwait(false);

        try
        {
            var result = await ImageHistoryService.GetFrameAsync(FrameId, CancellationToken.None).ConfigureAwait(false);
            if (!result.IsSuccessful)
            {
                _errorMessage = result.Error?.Message ?? "Unable to load archived frame.";
                Logger.LogWarning(result.Error, "Image history frame preview request failed for frame {FrameId}.", FrameId);
                return;
            }

            _viewModel = ImageHistoryViewModelMapper.CreateDetailViewModel(result.Value, NavigationManager);
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
            Logger.LogError(ex, "Unexpected failure while loading archived frame {FrameId}.", FrameId);
        }
        finally
        {
            _isLoading = false;
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }
    }

    private void NavigateBack()
        => NavigationManager.NavigateTo("/image-history");
}
