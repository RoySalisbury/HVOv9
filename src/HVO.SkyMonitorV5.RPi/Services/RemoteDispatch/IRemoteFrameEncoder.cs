#nullable enable
using HVO.SkyMonitorV5.RPi.Options;

namespace HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;

public interface IRemoteFrameEncoder
{
    RemoteFramePayload Encode(RemoteFrameEnvelope envelope, RemoteDispatchOptions options);
}
