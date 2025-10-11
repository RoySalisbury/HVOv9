#nullable enable
using System;
using System.IO;

namespace HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;

public sealed record RemoteFramePayload(byte[] Buffer, string ContentType, string FileExtension)
{
    public MemoryStream CreateStream()
        => new(Buffer ?? throw new ArgumentNullException(nameof(Buffer)), writable: false);

    public long Length => Buffer.LongLength;
}
