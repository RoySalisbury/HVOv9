#nullable enable
namespace HVO.SkyMonitorV5.RPi.Cameras.Rendering
{
    /// <summary>
    /// Ambient access to the <see cref="StarFieldEngine"/> used by the current capture/render pass.
    /// Cameras (mock or real) should call <see cref="Set"/> once per frame/cycle,
    /// and filters can read <see cref="Current"/> to reuse the exact same engine/projection.
    /// </summary>
    public interface IRenderEngineProvider
    {
        StarFieldEngine? Current { get; }
        void Set(StarFieldEngine engine);
    }
}