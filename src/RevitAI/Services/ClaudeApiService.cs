// RevitAI - AI-powered assistant for Autodesk Revit
// Copyright (C) 2025 Bryan McDonald
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using RevitAI.Models;

namespace RevitAI.Services;

/// <summary>
/// Service for communicating with the Claude Messages API.
/// Supports both streaming and non-streaming requests.
/// </summary>
public sealed class ClaudeApiService : IDisposable
{
    private const string ApiBaseUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    private readonly HttpClient _httpClient;
    private readonly ConfigurationService _configService;
    private CancellationTokenSource? _currentRequestCts;
    private readonly object _ctsLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Event raised when a streaming response completes, providing usage statistics.
    /// </summary>
    public event EventHandler<Usage>? StreamCompleted;

    public ClaudeApiService() : this(ConfigurationService.Instance)
    {
    }

    public ClaudeApiService(ConfigurationService configService)
    {
        _configService = configService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    /// <summary>
    /// Sends a message to Claude and returns the complete response.
    /// </summary>
    /// <param name="systemPrompt">Optional system prompt.</param>
    /// <param name="messages">Conversation history.</param>
    /// <param name="tools">Optional tool definitions.</param>
    /// <param name="settingsOverride">Optional per-request settings override.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Claude response.</returns>
    public async Task<ClaudeResponse> SendMessageAsync(
        string? systemPrompt,
        List<ClaudeMessage> messages,
        List<ToolDefinition>? tools = null,
        ApiSettings? settingsOverride = null,
        CancellationToken ct = default)
    {
        var settings = settingsOverride ?? _configService.DefaultApiSettings;

        var request = new ClaudeRequest
        {
            Model = settings.Model,
            MaxTokens = settings.MaxTokens,
            Temperature = settings.Temperature,
            System = systemPrompt,
            Messages = messages,
            Tools = tools,
            Stream = false
        };

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        RegisterCurrentRequest(linkedCts);

        try
        {
            var httpRequest = CreateHttpRequest(request);
            var response = await _httpClient.SendAsync(httpRequest, linkedCts.Token);

            var responseContent = await response.Content.ReadAsStringAsync(linkedCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode, responseContent);
            }

            var claudeResponse = JsonSerializer.Deserialize<ClaudeResponse>(responseContent, JsonOptions);
            if (claudeResponse == null)
            {
                throw new ClaudeApiException("Failed to parse response");
            }

            // Record token usage
            if (claudeResponse.Usage != null)
            {
                UsageTracker.Instance.RecordUsage(
                    claudeResponse.Usage.InputTokens,
                    claudeResponse.Usage.OutputTokens);
            }

            return claudeResponse;
        }
        finally
        {
            UnregisterCurrentRequest(linkedCts);
        }
    }

    /// <summary>
    /// Sends a message to Claude and streams the response.
    /// </summary>
    /// <param name="systemPrompt">Optional system prompt.</param>
    /// <param name="messages">Conversation history.</param>
    /// <param name="tools">Optional tool definitions.</param>
    /// <param name="settingsOverride">Optional per-request settings override.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of stream events.</returns>
    public async IAsyncEnumerable<StreamEvent> SendMessageStreamingAsync(
        string? systemPrompt,
        List<ClaudeMessage> messages,
        List<ToolDefinition>? tools = null,
        ApiSettings? settingsOverride = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var settings = settingsOverride ?? _configService.DefaultApiSettings;

        var request = new ClaudeRequest
        {
            Model = settings.Model,
            MaxTokens = settings.MaxTokens,
            Temperature = settings.Temperature,
            System = systemPrompt,
            Messages = messages,
            Tools = tools,
            Stream = true
        };

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        RegisterCurrentRequest(linkedCts);

        HttpResponseMessage? response = null;
        Usage? finalUsage = null;

        try
        {
            var httpRequest = CreateHttpRequest(request);
            response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                linkedCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(linkedCts.Token);
                throw CreateApiException(response.StatusCode, errorContent);
            }

            using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token);
            using var reader = new StreamReader(stream);

            string? currentEventType = null;
            var dataBuilder = new StringBuilder();

            while (!reader.EndOfStream)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(linkedCts.Token);

                if (string.IsNullOrEmpty(line))
                {
                    // Empty line indicates end of event
                    if (currentEventType != null && dataBuilder.Length > 0)
                    {
                        var eventData = dataBuilder.ToString();
                        var streamEvent = StreamEventParser.Parse(currentEventType, eventData);

                        if (streamEvent != null)
                        {
                            // Track usage from message_delta events
                            if (streamEvent is MessageDeltaEvent deltaEvent && deltaEvent.Usage != null)
                            {
                                finalUsage = deltaEvent.Usage;
                            }

                            yield return streamEvent;
                        }
                    }

                    currentEventType = null;
                    dataBuilder.Clear();
                    continue;
                }

                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    currentEventType = line.Substring(7);
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    dataBuilder.Append(line.Substring(6));
                }
            }
        }
        finally
        {
            response?.Dispose();
            UnregisterCurrentRequest(linkedCts);

            if (finalUsage != null)
            {
                // Record token usage for streaming responses
                UsageTracker.Instance.RecordUsage(
                    finalUsage.InputTokens,
                    finalUsage.OutputTokens);

                StreamCompleted?.Invoke(this, finalUsage);
            }
        }
    }

    /// <summary>
    /// Cancels the current request if one is in progress.
    /// </summary>
    public void CancelCurrentRequest()
    {
        lock (_ctsLock)
        {
            _currentRequestCts?.Cancel();
        }
    }

    private void RegisterCurrentRequest(CancellationTokenSource cts)
    {
        lock (_ctsLock)
        {
            _currentRequestCts = cts;
        }
    }

    private void UnregisterCurrentRequest(CancellationTokenSource cts)
    {
        lock (_ctsLock)
        {
            if (_currentRequestCts == cts)
            {
                _currentRequestCts = null;
            }
        }
    }

    private HttpRequestMessage CreateHttpRequest(ClaudeRequest request)
    {
        var apiKey = _configService.ApiKey
            ?? throw new ClaudeApiException("API key is not configured");

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", ApiVersion);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return httpRequest;
    }

    private static ClaudeApiException CreateApiException(System.Net.HttpStatusCode statusCode, string responseContent)
    {
        try
        {
            var errorResponse = JsonSerializer.Deserialize<ClaudeErrorResponse>(responseContent, JsonOptions);
            var message = errorResponse?.Error?.Message ?? "Unknown error";
            var errorType = errorResponse?.Error?.Type ?? "unknown";

            return statusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized =>
                    new ClaudeApiException($"Authentication failed: {message}", errorType, isRetryable: false),
                System.Net.HttpStatusCode.TooManyRequests =>
                    new ClaudeApiException($"Rate limit exceeded: {message}", errorType, isRetryable: true),
                System.Net.HttpStatusCode.BadRequest =>
                    new ClaudeApiException($"Invalid request: {message}", errorType, isRetryable: false),
                >= System.Net.HttpStatusCode.InternalServerError =>
                    new ClaudeApiException($"Server error: {message}", errorType, isRetryable: true),
                _ =>
                    new ClaudeApiException($"API error ({statusCode}): {message}", errorType, isRetryable: false)
            };
        }
        catch
        {
            return new ClaudeApiException($"API error ({statusCode}): {responseContent}");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        lock (_ctsLock)
        {
            _currentRequestCts?.Dispose();
            _currentRequestCts = null;
        }
    }
}

/// <summary>
/// Exception thrown when a Claude API call fails.
/// </summary>
public class ClaudeApiException : Exception
{
    /// <summary>
    /// Gets the error type from the API (e.g., "authentication_error", "rate_limit_error").
    /// </summary>
    public string ErrorType { get; }

    /// <summary>
    /// Gets whether this error is potentially retryable.
    /// </summary>
    public bool IsRetryable { get; }

    public ClaudeApiException(string message, string errorType = "unknown", bool isRetryable = false)
        : base(message)
    {
        ErrorType = errorType;
        IsRetryable = isRetryable;
    }
}
