using Microsoft.AspNetCore.Components;
using HVO.WebSite.RoofControllerV4.Models;
using HVO.WebSite.RoofControllerV4.Services;

namespace HVO.WebSite.RoofControllerV4.Components.Pages
{
    public partial class BlazorSample : ComponentBase, IDisposable
    {
        [Inject] private IWeatherService WeatherService { get; set; } = default!;
        [Inject] private ILogger<BlazorSample> Logger { get; set; } = default!;

        private WeatherData? weatherData;
        private bool isLoading = true;
        private Timer? refreshTimer;
        private bool isInitialized = false;

        protected override async Task OnInitializedAsync()
        {
            if (isInitialized)
                return;

            Logger.LogInformation("BlazorSample component initialized");
            isInitialized = true;
            
            // Load initial data
            await LoadWeatherDataAsync();
            
            // Set up auto-refresh every 15 seconds
            refreshTimer = new Timer(async _ => await RefreshWeatherData(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        }

        private async Task LoadWeatherDataAsync()
        {
            try
            {
                isLoading = true;
                StateHasChanged();
                
                weatherData = await WeatherService.GetCurrentWeatherAsync();
                Logger.LogInformation("Weather data loaded successfully");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading weather data");
                weatherData = null;
            }
            finally
            {
                isLoading = false;
                StateHasChanged();
            }
        }

        private async Task RefreshWeatherData()
        {
            try
            {
                weatherData = await WeatherService.GetCurrentWeatherAsync();
                await InvokeAsync(StateHasChanged);
                Logger.LogDebug("Weather data refreshed automatically");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error refreshing weather data");
            }
        }

        public void Dispose()
        {
            refreshTimer?.Dispose();
        }
    }
}
