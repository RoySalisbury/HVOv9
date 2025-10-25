using System;

namespace HVO.SkyMonitorV5.RPi.Components.Pages;

internal static class FrameViewUtilities
{
    public static string? BuildDataUri(ReadOnlySpan<byte> payload, string contentType)
    {
        if (payload.IsEmpty || string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var base64 = Convert.ToBase64String(payload);
        return FormattableString.Invariant($"data:{contentType};base64,{base64}");
    }

    public static string? BuildDataUri(byte[] payload, string contentType)
        => BuildDataUri(payload.AsSpan(), contentType);
}
