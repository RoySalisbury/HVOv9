using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

public enum RemoteDispatchMode
{
    None = 0,
    S3
}

public sealed class RemoteDispatchOptions
{
    public bool Enabled { get; set; }

    public RemoteDispatchMode Mode { get; set; } = RemoteDispatchMode.None;

    public RemoteDispatchImageFormat ImageFormat { get; set; } = RemoteDispatchImageFormat.Png;

    [MaxLength(128)]
    public string? S3Bucket { get; set; }

    [MaxLength(256)]
    public string? FanoutExchange { get; set; }

    [MaxLength(256)]
    public string? Endpoint { get; set; }

    [MaxLength(128)]
    public string? AccessKey { get; set; }

    [MaxLength(128)]
    public string? SecretKey { get; set; }

    public bool UseSsl { get; set; }

    public void Normalize()
    {
        if (!Enabled)
        {
            Mode = RemoteDispatchMode.None;
        }

        S3Bucket = string.IsNullOrWhiteSpace(S3Bucket) ? null : S3Bucket.Trim();
        FanoutExchange = string.IsNullOrWhiteSpace(FanoutExchange) ? null : FanoutExchange.Trim();
        Endpoint = string.IsNullOrWhiteSpace(Endpoint) ? null : Endpoint.Trim();
        AccessKey = string.IsNullOrWhiteSpace(AccessKey) ? null : AccessKey.Trim();
        SecretKey = string.IsNullOrWhiteSpace(SecretKey) ? null : SecretKey.Trim();

        if (Mode != RemoteDispatchMode.S3)
        {
            S3Bucket = null;
            FanoutExchange = null;
            Endpoint = null;
            AccessKey = null;
            SecretKey = null;
            UseSsl = false;
        }

        if (!Enum.IsDefined(typeof(RemoteDispatchImageFormat), ImageFormat))
        {
            ImageFormat = RemoteDispatchImageFormat.Png;
        }
    }
}
