#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;

namespace HVO.SkyMonitorV5.RPi.Cameras.Projection
{
    /// <summary>
    /// DI helpers to register a default camera+lens rig and expose <see cref="IImageProjector"/>.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers CameraSpec, LensSpec, RigSpec, and IImageProjector with defaults:
        /// Mock ASI174MM + Fujinon FE185C086HA-1 (Equisolid fisheye).
        /// </summary>
        public static IServiceCollection AddDefaultOpticsRig(this IServiceCollection services)
        {
            services.TryAddSingleton<RigSpec>(sp => RigPresets.MockAsi174_Fujinon);

            services.TryAddSingleton<IImageProjector>(sp =>
                RigFactory.CreateProjector(sp.GetRequiredService<RigSpec>()));

            return services;
        }
    }
}
