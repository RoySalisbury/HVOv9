using System;
using HVO;

namespace HVO.SkyMonitorV5.RPi.Services;

internal static class LocalApiResultExtensions
{
    public static Result<T> ToResult<T>(this T? payload, string failureMessage) where T : class
    {
        return payload is null
            ? Result<T>.Failure(new InvalidOperationException(failureMessage))
            : Result<T>.Success(payload);
    }
}
