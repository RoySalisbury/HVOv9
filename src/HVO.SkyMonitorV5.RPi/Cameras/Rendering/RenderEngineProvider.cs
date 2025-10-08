#nullable enable
using System.Threading;

namespace HVO.SkyMonitorV5.RPi.Cameras.Rendering
{
    /// <summary>
    /// Thread-safe implementation of <see cref="IRenderEngineProvider"/>.
    /// Keeps the most recent engine published by the camera loop.
    /// </summary>
    public sealed class RenderEngineProvider : IRenderEngineProvider
    {
        private StarFieldEngine? _current;

        public StarFieldEngine? Current => Volatile.Read(ref _current);

        public void Set(StarFieldEngine engine)
        {
            Volatile.Write(ref _current, engine);
        }
    }
}