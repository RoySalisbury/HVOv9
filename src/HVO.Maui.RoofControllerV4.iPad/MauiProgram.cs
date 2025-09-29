using System.Reflection;
using HVO.Maui.RoofControllerV4.iPad.Configuration;
using HVO.Maui.RoofControllerV4.iPad.Services;
using HVO.Maui.RoofControllerV4.iPad.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.Maui.RoofControllerV4.iPad;

public static class MauiProgram
{
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

		AddConfiguration(builder);
		ConfigureServices(builder.Services, builder.Configuration);

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

	private static void AddConfiguration(MauiAppBuilder builder)
	{
		var assembly = Assembly.GetExecutingAssembly();
		var resourceName = "HVO.Maui.RoofControllerV4.iPad.appsettings.json";
		using var stream = assembly.GetManifestResourceStream(resourceName);
		if (stream is not null)
		{
			((IConfigurationBuilder)builder.Configuration).AddJsonStream(stream);
		}
	}

	private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
	{
		services
			.AddOptions<RoofControllerApiOptions>()
			.Bind(configuration.GetSection("RoofControllerApi"))
			.ValidateDataAnnotations()
			.ValidateOnStart();

		services.AddHttpClient<IRoofControllerApiClient, RoofControllerApiClient>();

		services.AddSingleton<RoofControllerViewModel>();
		services.AddSingleton<MainPage>();
	}
}
