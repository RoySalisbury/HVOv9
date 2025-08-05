using HVO.NinaClient.Models;
using HVO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace HVO.NinaClient;

/// <summary>
/// Event arguments for NINA WebSocket events
/// </summary>
public class NinaEventArgs : EventArgs
{
    public NinaEventType EventType { get; }
    public object? EventData { get; }
    public NinaWebSocketResponse? RawResponse { get; }

    public NinaEventArgs(NinaEventType eventType, object? eventData, NinaWebSocketResponse? rawResponse)
    {
        EventType = eventType;
        EventData = eventData;
        RawResponse = rawResponse;
    }
}

/// <summary>
/// Interface for NINA WebSocket client
/// </summary>
public interface INinaWebSocketClient : IDisposable
{
    /// <summary>
    /// Event raised when any NINA event is received
    /// </summary>
    event EventHandler<NinaEventArgs>? EventReceived;

    /// <summary>
    /// Event raised when connection state changes
    /// </summary>
    event EventHandler<bool>? ConnectionStateChanged;

    /// <summary>
    /// Event raised when an error occurs
    /// </summary>
    event EventHandler<Exception>? ErrorOccurred;

    /// <summary>
    /// Gets the current connection state
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connect to the NINA WebSocket server
    /// </summary>
    Task<Result<bool>> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the NINA WebSocket server
    /// </summary>
    Task<Result<bool>> DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Move mount axis manually
    /// </summary>
    Task<Result<bool>> MoveMountAxisAsync(MountDirection direction, double rate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop mount axis movement
    /// </summary>
    Task<Result<bool>> StopMountMovementAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Start TPPA polar alignment
    /// </summary>
    Task<Result<bool>> StartTppaAlignmentAsync(TppaCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop TPPA polar alignment
    /// </summary>
    Task<Result<bool>> StopTppaAlignmentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause TPPA polar alignment
    /// </summary>
    Task<Result<bool>> PauseTppaAlignmentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume TPPA polar alignment
    /// </summary>
    Task<Result<bool>> ResumeTppaAlignmentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get target filter from networked filter wheel
    /// </summary>
    Task<Result<bool>> GetTargetFilterAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signal filter changed to networked filter wheel
    /// </summary>
    Task<Result<bool>> SignalFilterChangedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a test message to verify WebSocket connectivity
    /// </summary>
    Task<Result<bool>> SendTestMessageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discover available WebSocket endpoints on the NINA server
    /// </summary>
    Task<Result<string>> DiscoverWebSocketEndpointAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// NINA WebSocket client implementation with proper AsyncAPI channel separation
/// </summary>
public class NinaWebSocketClient : INinaWebSocketClient
{
    private readonly ILogger<NinaWebSocketClient> _logger;
    private readonly NinaWebSocketOptions _options;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    // ? CHANGE FOR 100% COMPLIANCE: Separate WebSocket connections per AsyncAPI channel
    private readonly ConcurrentDictionary<string, ClientWebSocket> _channelConnections = new();
    private readonly ConcurrentDictionary<string, Task> _channelReceiveTasks = new();
    
    private bool _disposed;
    private int _reconnectAttempts;

    public event EventHandler<NinaEventArgs>? EventReceived;
    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<Exception>? ErrorOccurred;

    public bool IsConnected => _channelConnections.Values.Any(ws => ws.State == WebSocketState.Open);

    public NinaWebSocketClient(ILogger<NinaWebSocketClient> logger, IOptions<NinaWebSocketOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<Result<bool>> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Result<bool>.Failure(new ObjectDisposedException(nameof(NinaWebSocketClient)));
        }

        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                _logger.LogDebug("Already connected to NINA WebSocket server");
                return Result<bool>.Success(true);
            }

            _logger.LogInformation("Connecting to NINA WebSocket server at {BaseUri}", _options.BaseUri);

            // FIXED: Connect to all AsyncAPI channels as specified in advanced-api-websockets-asyncapi-source.json
            var channelsToConnect = new[] { "/socket", "/mount", "/tppa", "/filterwheel" };
            var connectionTasks = channelsToConnect.Select(channel => ConnectToChannelAsync(channel, cancellationToken)).ToArray();
            
            var results = await Task.WhenAll(connectionTasks);
            
            if (results.All(r => r.IsSuccessful))
            {
                _logger.LogInformation("Successfully connected to all NINA WebSocket channels");
                _reconnectAttempts = 0;
                ConnectionStateChanged?.Invoke(this, true);
                return Result<bool>.Success(true);
            }
            else
            {
                var failures = results.Where(r => !r.IsSuccessful).Select(r => r.Error?.Message).ToArray();
                var combinedError = string.Join("; ", failures);
                _logger.LogError("Failed to connect to some NINA WebSocket channels: {Errors}", combinedError);
                return Result<bool>.Failure(new InvalidOperationException($"Channel connection failures: {combinedError}"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to NINA WebSocket server");
            ErrorOccurred?.Invoke(this, ex);
            return Result<bool>.Failure(ex);
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private async Task<Result<bool>> ConnectToChannelAsync(string channel, CancellationToken cancellationToken)
    {
        try
        {
            var webSocket = new ClientWebSocket();
            webSocket.Options.KeepAliveInterval = TimeSpan.FromMilliseconds(_options.KeepAliveIntervalMs);

            // Use the exact AsyncAPI specification format: ws://localhost:1888/v2 + channel
            var uri = new Uri($"{_options.BaseUri}{channel}");
            var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ConnectionTimeoutMs);

            _logger.LogDebug("Attempting to connect to NINA WebSocket channel: {Uri}", uri);
            await webSocket.ConnectAsync(uri, timeoutCts.Token);

            _channelConnections[channel] = webSocket;
            
            // Start receiving messages for this channel
            _channelReceiveTasks[channel] = ReceiveChannelMessagesAsync(channel, _cancellationTokenSource.Token);

            _logger.LogInformation("✅ Connected to NINA WebSocket channel: {Channel} at {Uri}", channel, uri);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to connect to NINA WebSocket channel: {Channel} - {Error}", channel, ex.Message);
            return Result<bool>.Failure(ex);
        }
    }

    public async Task<Result<bool>> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Result<bool>.Failure(new ObjectDisposedException(nameof(NinaWebSocketClient)));
        }

        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (!IsConnected)
            {
                _logger.LogDebug("Already disconnected from NINA WebSocket server");
                return Result<bool>.Success(true);
            }

            _logger.LogInformation("Disconnecting from NINA WebSocket server");

            // Disconnect from all AsyncAPI channels
            var disconnectionTasks = _channelConnections.Keys.Select(channel => DisconnectFromChannelAsync(channel, cancellationToken)).ToArray();
            await Task.WhenAll(disconnectionTasks);

            // Wait for all receive tasks to complete
            var receiveTasks = _channelReceiveTasks.Values.ToArray();
            if (receiveTasks.Length > 0)
            {
                await Task.WhenAll(receiveTasks);
            }

            _channelConnections.Clear();
            _channelReceiveTasks.Clear();

            _logger.LogInformation("Successfully disconnected from NINA WebSocket server");
            ConnectionStateChanged?.Invoke(this, false);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disconnect from NINA WebSocket server");
            ErrorOccurred?.Invoke(this, ex);
            return Result<bool>.Failure(ex);
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private async Task DisconnectFromChannelAsync(string channel, CancellationToken cancellationToken)
    {
        try
        {
            if (_channelConnections.TryGetValue(channel, out var webSocket))
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", cancellationToken);
                }
                webSocket.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disconnecting from channel: {Channel}", channel);
        }
    }

    public async Task<Result<bool>> MoveMountAxisAsync(MountDirection direction, double rate, CancellationToken cancellationToken = default)
    {
        try
        {
            var command = new MountAxisMoveCommand
            {
                Direction = direction.ToString().ToLowerInvariant(),
                Rate = rate
            };

            // CORRECTED: Send to /mount channel as per AsyncAPI specification
            var result = await SendToChannelAsync("/mount", JsonSerializer.Serialize(command), cancellationToken);
            if (result.IsSuccessful)
            {
                _logger.LogDebug("Mount axis move command sent - Direction: {Direction}, Rate: {Rate}", direction, rate);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send mount axis move command");
            ErrorOccurred?.Invoke(this, ex);
            return Result<bool>.Failure(ex);
        }
    }

    public Task<Result<bool>> StopMountMovementAsync(CancellationToken cancellationToken = default)
    {
        // Mount movement stops automatically after 2 seconds without commands
        // But we can explicitly stop by not sending any more commands
        _logger.LogDebug("Mount movement will stop automatically after 2 seconds of inactivity");
        return Task.FromResult(Result<bool>.Success(true));
    }

    public async Task<Result<bool>> StartTppaAlignmentAsync(TppaCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            command = command with { Action = "start-alignment" };
            // CORRECTED: Send to /tppa channel as per AsyncAPI specification
            var result = await SendToChannelAsync("/tppa", JsonSerializer.Serialize(command), cancellationToken);
            if (result.IsSuccessful)
            {
                _logger.LogInformation("TPPA alignment started with command: {Command}", JsonSerializer.Serialize(command));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start TPPA alignment");
            ErrorOccurred?.Invoke(this, ex);
            return Result<bool>.Failure(ex);
        }
    }

    public async Task<Result<bool>> StopTppaAlignmentAsync(CancellationToken cancellationToken = default)
    {
        return await SendTppaCommandAsync("stop-alignment", cancellationToken);
    }

    public async Task<Result<bool>> PauseTppaAlignmentAsync(CancellationToken cancellationToken = default)
    {
        return await SendTppaCommandAsync("pause-alignment", cancellationToken);
    }

    public async Task<Result<bool>> ResumeTppaAlignmentAsync(CancellationToken cancellationToken = default)
    {
        return await SendTppaCommandAsync("resume-alignment", cancellationToken);
    }

    public async Task<Result<bool>> GetTargetFilterAsync(CancellationToken cancellationToken = default)
    {
        // CORRECTED: Send to /filterwheel channel as per AsyncAPI specification
        return await SendToChannelAsync("/filterwheel", "get-target-filter", cancellationToken);
    }

    public async Task<Result<bool>> SignalFilterChangedAsync(CancellationToken cancellationToken = default)
    {
        // CORRECTED: Send to /filterwheel channel as per AsyncAPI specification
        return await SendToChannelAsync("/filterwheel", "filter-changed", cancellationToken);
    }

    public async Task<Result<bool>> SendTestMessageAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending test message to NINA WebSocket");
        
        try
        {
            // Send test messages to all channels to verify connectivity
            var channels = new[] { "/socket", "/mount", "/tppa", "/filterwheel" };
            var testResults = new List<Result<bool>>();
            
            foreach (var channel in channels)
            {
                try
                {
                    var result = await SendToChannelAsync(channel, "ping", cancellationToken);
                    testResults.Add(result);
                    
                    if (result.IsSuccessful)
                    {
                        _logger.LogInformation("✅ Test message sent successfully to channel {Channel}", channel);
                    }
                    else
                    {
                        _logger.LogWarning("❌ Failed to send test message to channel {Channel}: {Error}", channel, result.Error?.Message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error sending test message to channel {Channel}", channel);
                    testResults.Add(Result<bool>.Failure(ex));
                }
            }
            
            var successfulTests = testResults.Count(r => r.IsSuccessful);
            _logger.LogInformation("🎯 WebSocket test complete: {Successful}/{Total} channels responded", successfulTests, testResults.Count);
            
            return successfulTests > 0 ? Result<bool>.Success(true) : Result<bool>.Failure(new InvalidOperationException("No channels responded to test messages"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebSocket test");
            return Result<bool>.Failure(ex);
        }
    }

    public async Task<Result<string>> DiscoverWebSocketEndpointAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔍 Discovering available NINA WebSocket endpoints...");
        
        var baseUrl = _options.BaseUri.Replace("ws://", "").Replace("wss://", "");
        var parts = baseUrl.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 ? parts[1].Split('/')[0] : "1888";
        
        // Test the exact AsyncAPI specification endpoints
        var channelsToTest = new[] { "/socket", "/mount", "/tppa", "/filterwheel" };
        var workingEndpoints = new List<string>();
        
        foreach (var channel in channelsToTest)
        {
            var endpoint = $"ws://{host}:{port}/v2{channel}";
            
            try
            {
                using var testSocket = new ClientWebSocket();
                var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(2000); // Short timeout for discovery
                
                _logger.LogDebug("Testing WebSocket endpoint: {Endpoint}", endpoint);
                await testSocket.ConnectAsync(new Uri(endpoint), timeoutCts.Token);
                
                workingEndpoints.Add(endpoint);
                _logger.LogInformation("✅ Working WebSocket endpoint found: {Endpoint}", endpoint);
                
                // Close the test connection
                await testSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Discovery test", CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("❌ Endpoint {Endpoint} failed: {Error}", endpoint, ex.Message);
            }
        }
        
        if (workingEndpoints.Count > 0)
        {
            var result = string.Join(", ", workingEndpoints);
            _logger.LogInformation("🎯 Discovery complete. Working AsyncAPI channels: {Endpoints}", result);
            return Result<string>.Success(result);
        }
        else
        {
            var errorMsg = "No working AsyncAPI WebSocket channels found. Check that NINA is running with Advanced API plugin enabled.";
            _logger.LogError("❌ {ErrorMessage}", errorMsg);
            return Result<string>.Failure(new InvalidOperationException(errorMsg));
        }
    }

    private async Task<Result<bool>> SendTppaCommandAsync(string action, CancellationToken cancellationToken)
    {
        try
        {
            var command = new TppaCommand { Action = action };
            // CORRECTED: Send to /tppa channel as per AsyncAPI specification
            var result = await SendToChannelAsync("/tppa", JsonSerializer.Serialize(command), cancellationToken);
            if (result.IsSuccessful)
            {
                _logger.LogInformation("TPPA command sent: {Action}", action);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send TPPA command: {Action}", action);
            ErrorOccurred?.Invoke(this, ex);
            return Result<bool>.Failure(ex);
        }
    }

    // Send messages to the correct AsyncAPI channel connection
    private async Task<Result<bool>> SendToChannelAsync(string channel, string message, CancellationToken cancellationToken)
    {
        if (!_channelConnections.TryGetValue(channel, out var webSocket))
        {
            return Result<bool>.Failure(new InvalidOperationException($"Not connected to channel: {channel}"));
        }

        if (webSocket.State != WebSocketState.Open)
        {
            return Result<bool>.Failure(new InvalidOperationException($"WebSocket channel {channel} is not open (state: {webSocket.State})"));
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            var buffer = new ArraySegment<byte>(bytes);

            await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
            _logger.LogTrace("Message sent to NINA WebSocket channel {Channel}: {Message}", channel, message);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WebSocket message to channel {Channel}", channel);
            return Result<bool>.Failure(ex);
        }
    }

    // ? CHANGE FOR 100% COMPLIANCE: Separate message receiving per channel
    private async Task ReceiveChannelMessagesAsync(string channel, CancellationToken cancellationToken)
    {
        var buffer = new byte[_options.BufferSize];

        try
        {
            if (!_channelConnections.TryGetValue(channel, out var webSocket))
            {
                _logger.LogWarning("WebSocket connection not found for channel: {Channel}", channel);
                return;
            }

            _logger.LogDebug("Starting message receive loop for channel: {Channel}", channel);

            while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        _logger.LogTrace("Received message on channel {Channel}: {Message}", channel, message);
                        _ = Task.Run(() => ProcessChannelMessageAsync(channel, message));
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("WebSocket channel {Channel} closed by server", channel);
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("WebSocket receive operation cancelled for channel: {Channel}", channel);
                    break;
                }
                catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                {
                    _logger.LogWarning("WebSocket channel {Channel} closed prematurely, attempting reconnect", channel);
                    await AttemptChannelReconnectAsync(channel);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error receiving WebSocket message on channel: {Channel}", channel);
                    ErrorOccurred?.Invoke(this, ex);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in WebSocket receive loop for channel: {Channel}", channel);
            ErrorOccurred?.Invoke(this, ex);
        }
        finally
        {
            _logger.LogDebug("Message receive loop ended for channel: {Channel}", channel);
            if (_channelConnections.Values.All(ws => ws.State != WebSocketState.Open))
            {
                ConnectionStateChanged?.Invoke(this, false);
            }
        }
    }

    private Task ProcessChannelMessageAsync(string channel, string message)
    {
        try
        {
            _logger.LogTrace("Processing message from channel {Channel}: {Message}", channel, message);

            // FIXED: Handle different NINA WebSocket message formats
            // First, try to parse as direct event format (common in NINA)
            if (TryParseDirectEventMessage(message, out var eventType, out var eventData))
            {
                var eventArgs = new NinaEventArgs(eventType, eventData, null);
                EventReceived?.Invoke(this, eventArgs);
                _logger.LogDebug("Processed direct NINA event: {EventType}", eventType);
                return Task.CompletedTask;
            }

            // Try standard NINA API response format
            var response = JsonSerializer.Deserialize<NinaWebSocketResponse>(message);
            if (response == null)
            {
                _logger.LogWarning("Failed to deserialize WebSocket message from channel {Channel}: {Message}", channel, message);
                return Task.CompletedTask;
            }

            var ninaEventType = NinaEventType.Unknown;
            object? ninaEventData = null;

            // Parse the response based on the event type
            if (response.Response is JsonElement responseElement)
            {
                if (responseElement.ValueKind == JsonValueKind.Object && responseElement.TryGetProperty("Event", out var eventProperty))
                {
                    var eventName = eventProperty.GetString();
                    ninaEventType = ParseEventType(eventName);
                    ninaEventData = ParseEventData(eventName, responseElement);
                }
                else if (responseElement.ValueKind == JsonValueKind.String)
                {
                    // Simple string response
                    ninaEventData = responseElement.GetString();
                }
            }

            var eventArgs2 = new NinaEventArgs(ninaEventType, ninaEventData, response);
            EventReceived?.Invoke(this, eventArgs2);

            _logger.LogDebug("Processed NINA event from channel {Channel}: {EventType}", channel, ninaEventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WebSocket message from channel {Channel}: {Message}", channel, message);
            ErrorOccurred?.Invoke(this, ex);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Try to parse direct NINA event messages (common format)
    /// </summary>
    private bool TryParseDirectEventMessage(string message, out NinaEventType eventType, out object? eventData)
    {
        eventType = NinaEventType.Unknown;
        eventData = null;

        try
        {
            // Handle simple event string format: "EVENT-NAME"
            if (!message.StartsWith("{") && !message.StartsWith("["))
            {
                eventType = ParseEventType(message.Trim().Trim('"'));
                eventData = message;
                return eventType != NinaEventType.Unknown;
            }

            // Handle JSON object with Event property at root level
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;

            if (root.TryGetProperty("Event", out var eventProperty))
            {
                var eventName = eventProperty.GetString();
                eventType = ParseEventType(eventName);
                eventData = ParseEventData(eventName, root);
                return true;
            }

            // Handle array format or other structures
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to parse as direct event message: {Message}", message);
            return false;
        }
    }

    private async Task AttemptChannelReconnectAsync(string channel)
    {
        if (_reconnectAttempts >= _options.MaxReconnectAttempts)
        {
            _logger.LogError("Maximum reconnection attempts ({MaxAttempts}) reached for channel: {Channel}", _options.MaxReconnectAttempts, channel);
            return;
        }

        _reconnectAttempts++;
        _logger.LogInformation("Attempting to reconnect channel {Channel} (attempt {Attempt}/{MaxAttempts})", 
            channel, _reconnectAttempts, _options.MaxReconnectAttempts);

        await Task.Delay(_options.ReconnectDelayMs, _cancellationTokenSource.Token);

        var result = await ConnectToChannelAsync(channel, _cancellationTokenSource.Token);
        if (!result.IsSuccessful)
        {
            _logger.LogWarning("Reconnection attempt {Attempt} failed for channel {Channel}: {Error}", 
                _reconnectAttempts, channel, result.Error?.Message);
        }
        else
        {
            _logger.LogInformation("Successfully reconnected to channel {Channel}", channel);
        }
    }

    private static NinaEventType ParseEventType(string? eventName)
    {
        return eventName switch
        {
            "API-CAPTURE-FINISHED" => NinaEventType.ApiCaptureFinished,
            "AUTOFOCUS-FINISHED" => NinaEventType.AutofocusFinished,
            "CAMERA-CONNECTED" => NinaEventType.CameraConnected,
            "CAMERA-DISCONNECTED" => NinaEventType.CameraDisconnected,
            "CAMERA-DOWNLOAD-TIMEOUT" => NinaEventType.CameraDownloadTimeout,
            "DOME-CONNECTED" => NinaEventType.DomeConnected,
            "DOME-DISCONNECTED" => NinaEventType.DomeDisconnected,
            "DOME-SHUTTER-CLOSED" => NinaEventType.DomeShutterClosed,
            "DOME-SHUTTER-OPENED" => NinaEventType.DomeShutterOpened,
            "DOME-HOMED" => NinaEventType.DomeHomed,
            "DOME-PARKED" => NinaEventType.DomeParked,
            "DOME-STOPPED" => NinaEventType.DomeStopped,
            "DOME-SLEWED" => NinaEventType.DomeSlewed,
            "DOME-SYNCED" => NinaEventType.DomeSynced,
            "FILTERWHEEL-CONNECTED" => NinaEventType.FilterWheelConnected,
            "FILTERWHEEL-DISCONNECTED" => NinaEventType.FilterWheelDisconnected,
            "FILTERWHEEL-CHANGED" => NinaEventType.FilterWheelChanged,
            "FLAT-CONNECTED" => NinaEventType.FlatConnected,
            "FLAT-DISCONNECTED" => NinaEventType.FlatDisconnected,
            "FLAT-LIGHT-TOGGLED" => NinaEventType.FlatLightToggled,
            "FLAT-COVER-OPENED" => NinaEventType.FlatCoverOpened,
            "FLAT-COVER-CLOSED" => NinaEventType.FlatCoverClosed,
            "FLAT-BRIGHTNESS-CHANGED" => NinaEventType.FlatBrightnessChanged,
            "FOCUSER-CONNECTED" => NinaEventType.FocuserConnected,
            "FOCUSER-DISCONNECTED" => NinaEventType.FocuserDisconnected,
            "FOCUSER-USER-FOCUSED" => NinaEventType.FocuserUserFocused,
            "GUIDER-CONNECTED" => NinaEventType.GuiderConnected,
            "GUIDER-DISCONNECTED" => NinaEventType.GuiderDisconnected,
            "GUIDER-START" => NinaEventType.GuiderStart,
            "GUIDER-STOP" => NinaEventType.GuiderStop,
            "GUIDER-DITHER" => NinaEventType.GuiderDither,
            "MOUNT-CONNECTED" => NinaEventType.MountConnected,
            "MOUNT-DISCONNECTED" => NinaEventType.MountDisconnected,
            "MOUNT-BEFORE-FLIP" => NinaEventType.MountBeforeFlip,
            "MOUNT-AFTER-FLIP" => NinaEventType.MountAfterFlip,
            "MOUNT-HOMED" => NinaEventType.MountHomed,
            "MOUNT-PARKED" => NinaEventType.MountParked,
            "MOUNT-UNPARKED" => NinaEventType.MountUnparked,
            "MOUNT-CENTER" => NinaEventType.MountCenter,
            "PROFILE-ADDED" => NinaEventType.ProfileAdded,
            "PROFILE-CHANGED" => NinaEventType.ProfileChanged,
            "PROFILE-REMOVED" => NinaEventType.ProfileRemoved,
            "ROTATOR-CONNECTED" => NinaEventType.RotatorConnected,
            "ROTATOR-DISCONNECTED" => NinaEventType.RotatorDisconnected,
            "ROTATOR-SYNCED" => NinaEventType.RotatorSynced,
            "ROTATOR-MOVED" => NinaEventType.RotatorMoved,
            "ROTATOR-MOVED-MECHANICAL" => NinaEventType.RotatorMovedMechanical,
            "SAFETY-CONNECTED" => NinaEventType.SafetyConnected,
            "SAFETY-DISCONNECTED" => NinaEventType.SafetyDisconnected,
            "SAFETY-CHANGED" => NinaEventType.SafetyChanged,
            "SEQUENCE-STARTING" => NinaEventType.SequenceStarting,
            "SEQUENCE-FINISHED" => NinaEventType.SequenceFinished,
            "SEQUENCE-ENTITY-FAILED" => NinaEventType.SequenceEntityFailed,
            "SWITCH-CONNECTED" => NinaEventType.SwitchConnected,
            "SWITCH-DISCONNECTED" => NinaEventType.SwitchDisconnected,
            "WEATHER-CONNECTED" => NinaEventType.WeatherConnected,
            "WEATHER-DISCONNECTED" => NinaEventType.WeatherDisconnected,
            "ADV-SEQ-START" => NinaEventType.AdvSeqStart,
            "ADV-SEQ-STOP" => NinaEventType.AdvSeqStop,
            "ERROR-AF" => NinaEventType.ErrorAf,
            "ERROR-PLATESOLVE" => NinaEventType.ErrorPlatesolve,
            "IMAGE-SAVE" => NinaEventType.ImageSave,
            "STACK-UPDATED" => NinaEventType.StackUpdated,
            "TS-WAITSTART" => NinaEventType.TsWaitStart,
            "TS-NEWTARGETSTART" => NinaEventType.TsNewTargetStart,
            "TS-TARGETSTART" => NinaEventType.TsTargetStart,
            _ => NinaEventType.Unknown
        };
    }

    private static object? ParseEventData(string? eventName, JsonElement responseElement)
    {
        try
        {
            return eventName switch
            {
                "FILTERWHEEL-CHANGED" => JsonSerializer.Deserialize<FilterChangedResponse>(responseElement),
                "FLAT-BRIGHTNESS-CHANGED" => JsonSerializer.Deserialize<FlatBrightnessChangedResponse>(responseElement),
                "SAFETY-CHANGED" => JsonSerializer.Deserialize<SafetyChangedResponse>(responseElement),
                "STACK-UPDATED" => JsonSerializer.Deserialize<StackUpdatedResponse>(responseElement),
                "TS-NEWTARGETSTART" or "TS-TARGETSTART" => JsonSerializer.Deserialize<TargetStartResponse>(responseElement),
                "ROTATOR-MOVED" or "ROTATOR-MOVED-MECHANICAL" => JsonSerializer.Deserialize<RotatorMovedResponse>(responseElement),
                "SEQUENCE-ENTITY-FAILED" => JsonSerializer.Deserialize<SequenceEntityFailedResponse>(responseElement),
                "IMAGE-SAVE" => ParseImageSaveEvent(responseElement),
                _ => JsonSerializer.Deserialize<SimpleEventResponse>(responseElement)
            };
        }
        catch
        {
            // Fallback to simple event response
            return JsonSerializer.Deserialize<SimpleEventResponse>(responseElement);
        }
    }

    private static object? ParseImageSaveEvent(JsonElement responseElement)
    {
        try
        {
            if (responseElement.TryGetProperty("ImageStatistics", out var statsElement))
            {
                return JsonSerializer.Deserialize<ImageStatistics>(statsElement);
            }
        }
        catch
        {
            // Ignore parsing errors for image statistics
        }

        return JsonSerializer.Deserialize<SimpleEventResponse>(responseElement);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _cancellationTokenSource.Cancel();
        
        // ? CHANGE FOR 100% COMPLIANCE: Dispose all channel connections
        foreach (var webSocket in _channelConnections.Values)
        {
            webSocket?.Dispose();
        }
        _channelConnections.Clear();
        _channelReceiveTasks.Clear();
        
        _connectionSemaphore.Dispose();
        _cancellationTokenSource.Dispose();

        GC.SuppressFinalize(this);
    }
}
