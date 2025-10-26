#nullable enable
using System;
using System.Globalization;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Minio;

namespace HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;

/// <inheritdoc />
public sealed class MinioClientProvider : IMinioClientProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, IMinioClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<MinioClientProvider> _logger;

    public MinioClientProvider(ILogger<MinioClientProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IMinioClient GetClient(string endpoint, string accessKey, string secretKey, bool useSsl)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint is required for MinIO client creation.", nameof(endpoint));
        }

        if (string.IsNullOrWhiteSpace(accessKey))
        {
            throw new ArgumentException("Access key is required for MinIO client creation.", nameof(accessKey));
        }

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new ArgumentException("Secret key is required for MinIO client creation.", nameof(secretKey));
        }

        var key = string.Create(endpoint.Length + accessKey.Length + 6, (endpoint, accessKey, useSsl), static (span, state) =>
        {
            var (endpointValue, accessKeyValue, ssl) = state;
            endpointValue.AsSpan().CopyTo(span);
            var offset = endpointValue.Length;
            span[offset++] = '|';
            accessKeyValue.AsSpan().CopyTo(span[offset..]);
            offset += accessKeyValue.Length;
            span[offset++] = '|';
            span[offset++] = ssl ? '1' : '0';
        });

        return _clients.GetOrAdd(key, _ => BuildClient(endpoint, accessKey, secretKey, useSsl));
    }

    private IMinioClient BuildClient(string endpoint, string accessKey, string secretKey, bool useSsl)
    {
        var (normalizedEndpoint, normalizedSsl) = NormalizeEndpoint(endpoint, useSsl);

        _logger.LogDebug("Creating MinIO client for endpoint {Endpoint} (normalized: {Normalized}, SSL={UseSsl}).", endpoint, normalizedEndpoint, normalizedSsl);

        var builder = new MinioClient()
            .WithEndpoint(normalizedEndpoint)
            .WithCredentials(accessKey, secretKey);

        if (normalizedSsl)
        {
            builder = builder.WithSSL();
        }

        return builder.Build();
    }

    private static (string endpoint, bool useSsl) NormalizeEndpoint(string endpoint, bool useSsl)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return (endpoint, useSsl);
        }

        var trimmed = endpoint.Trim();

        // If endpoint includes scheme, parse and convert to host[:port], updating SSL accordingly
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                var host = uri.Host;
                var portPart = uri.IsDefaultPort ? string.Empty : ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
                var hostPort = string.Concat(host, portPart);
                var ssl = useSsl || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
                return (hostPort, ssl);
            }

            // Fallback: strip known scheme prefixes if Uri parsing failed
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("http://".Length);
            }
            else if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("https://".Length);
                useSsl = true;
            }
        }

        return (trimmed, useSsl);
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            if (client is IAsyncDisposable asyncDisposable)
            {
                try
                {
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to dispose MinIO client cleanly (async).");
                }
            }

            if (client is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to dispose MinIO client cleanly (sync).");
                }
            }
        }

        _clients.Clear();
    }
}
