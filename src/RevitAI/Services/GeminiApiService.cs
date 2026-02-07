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
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using RevitAI.Models;

namespace RevitAI.Services;

/// <summary>
/// AI provider implementation for the Google Gemini API.
/// Translates between the canonical internal types (ClaudeMessage, StreamEvent, etc.)
/// and Gemini's wire format.
/// </summary>
public sealed class GeminiApiService : IAiProvider
{
    private const string ApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    private readonly HttpClient _httpClient;
    private readonly ConfigurationService _configService;
    private CancellationTokenSource? _currentRequestCts;
    private readonly object _ctsLock = new();

    // Gemini doesn't assign IDs to function calls, so we generate synthetic ones
    // and track the mapping from ID → function name for tool results.
    private readonly Dictionary<string, string> _toolCallIdToName = new();

    // Gemini 3 Pro requires thoughtSignature to be echoed back alongside function calls
    // in conversation history. Track by tool call ID.
    private readonly Dictionary<string, string> _toolCallIdToThoughtSig = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc/>
    public string ProviderName => "Gemini";

    /// <inheritdoc/>
    public event EventHandler<Usage>? StreamCompleted;

    public GeminiApiService(ConfigurationService configService)
    {
        _configService = configService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    /// <inheritdoc/>
    public async Task<ClaudeResponse> SendMessageAsync(
        string? systemPrompt,
        List<ClaudeMessage> messages,
        List<ToolDefinition>? tools = null,
        ApiSettings? settingsOverride = null,
        CancellationToken ct = default)
    {
        var settings = settingsOverride ?? _configService.DefaultApiSettings;
        var request = BuildGeminiRequest(systemPrompt, messages, tools, settings);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        RegisterCurrentRequest(linkedCts);

        try
        {
            var apiKey = _configService.GeminiApiKey
                ?? throw new ClaudeApiException("Gemini API key is not configured", "authentication_error");

            var url = $"{ApiBaseUrl}/models/{settings.Model}:generateContent?key={apiKey}";
            var json = JsonSerializer.Serialize(request, JsonOptions);
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(httpRequest, linkedCts.Token);
            var responseContent = await response.Content.ReadAsStringAsync(linkedCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode, responseContent);
            }

            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseContent, JsonReadOptions);
            if (geminiResponse == null)
            {
                throw new ClaudeApiException("Failed to parse Gemini response");
            }

            var claudeResponse = ConvertResponse(geminiResponse);

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

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamEvent> SendMessageStreamingAsync(
        string? systemPrompt,
        List<ClaudeMessage> messages,
        List<ToolDefinition>? tools = null,
        ApiSettings? settingsOverride = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var settings = settingsOverride ?? _configService.DefaultApiSettings;
        var request = BuildGeminiRequest(systemPrompt, messages, tools, settings);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        RegisterCurrentRequest(linkedCts);

        HttpResponseMessage? response = null;
        Usage? finalUsage = null;

        try
        {
            var apiKey = _configService.GeminiApiKey
                ?? throw new ClaudeApiException("Gemini API key is not configured", "authentication_error");

            var url = $"{ApiBaseUrl}/models/{settings.Model}:streamGenerateContent?alt=sse&key={apiKey}";
            var json = JsonSerializer.Serialize(request, JsonOptions);
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

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

            // Track content block index for stream events
            int contentBlockIndex = 0;
            bool messageStarted = false;
            bool anyFunctionCalls = false;
            // Track the last-seen thoughtSignature in this stream turn.
            // Gemini only attaches it to the first function call; subsequent
            // calls in the same turn need it propagated.
            string? lastThoughtSignature = null;
            // Collect raw lines for fallback if SSE parsing finds nothing
            var rawLines = new List<string>();

            while (!reader.EndOfStream)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(linkedCts.Token);

                if (string.IsNullOrEmpty(line))
                    continue;

                rawLines.Add(line);

                // Extract JSON from SSE data lines, handling various formats:
                //   "data: {...}"  (standard SSE)
                //   "data:{...}"   (no space)
                //   "{...}"        (plain JSON, no SSE wrapper)
                string data;
                if (line.StartsWith("data: ", StringComparison.Ordinal))
                    data = line.Substring(6);
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                    data = line.Substring(5);
                else if (line.StartsWith("{", StringComparison.Ordinal))
                    data = line;
                else
                    continue;

                if (string.IsNullOrWhiteSpace(data))
                    continue;

                GeminiResponse? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<GeminiResponse>(data, JsonReadOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (chunk == null)
                    continue;

                // Emit message_start on first chunk
                if (!messageStarted)
                {
                    messageStarted = true;
                    yield return new MessageStartEvent
                    {
                        Message = new ClaudeResponse
                        {
                            Id = $"gemini_{Guid.NewGuid():N}",
                            Type = "message",
                            Role = "assistant",
                            Content = new List<ContentBlock>()
                        }
                    };
                }

                // Track usage from each chunk (last one wins)
                if (chunk.UsageMetadata != null)
                {
                    finalUsage = new Usage
                    {
                        InputTokens = chunk.UsageMetadata.PromptTokenCount,
                        OutputTokens = chunk.UsageMetadata.CandidatesTokenCount
                    };
                }

                var candidate = chunk.Candidates?.FirstOrDefault();
                if (candidate?.Content?.Parts == null)
                    continue;

                foreach (var part in candidate.Content.Parts)
                {
                    if (part.Text != null && part.Text.Length > 0)
                    {
                        // Text content — skip empty strings (Gemini sends "" alongside tool calls)
                        yield return new ContentBlockStartEvent
                        {
                            Index = contentBlockIndex,
                            ContentBlock = new TextContentBlock { Text = "" }
                        };

                        yield return new ContentBlockDeltaEvent
                        {
                            Index = contentBlockIndex,
                            Delta = new TextDelta { Text = part.Text }
                        };

                        yield return new ContentBlockStopEvent
                        {
                            Index = contentBlockIndex
                        };

                        contentBlockIndex++;
                    }
                    else if (part.FunctionCall != null)
                    {
                        // Function call - emit as tool use block
                        anyFunctionCalls = true;
                        var toolCallId = $"gemini_call_{Guid.NewGuid():N}";
                        _toolCallIdToName[toolCallId] = part.FunctionCall.Name;

                        // Capture or propagate thoughtSignature
                        if (part.ThoughtSignature != null)
                            lastThoughtSignature = part.ThoughtSignature;
                        // Use the part's own signature, or fall back to the last-seen one
                        var effectiveSig = part.ThoughtSignature ?? lastThoughtSignature;
                        if (effectiveSig != null)
                            _toolCallIdToThoughtSig[toolCallId] = effectiveSig;

                        yield return new ContentBlockStartEvent
                        {
                            Index = contentBlockIndex,
                            ContentBlock = new ToolUseBlock
                            {
                                Id = toolCallId,
                                Name = part.FunctionCall.Name,
                                Input = JsonDocument.Parse("{}").RootElement
                            }
                        };

                        // Emit the full args as a single JSON delta
                        var argsJson = part.FunctionCall.Args.ValueKind != JsonValueKind.Undefined
                            ? part.FunctionCall.Args.GetRawText()
                            : "{}";

                        yield return new ContentBlockDeltaEvent
                        {
                            Index = contentBlockIndex,
                            Delta = new InputJsonDelta { PartialJson = argsJson }
                        };

                        yield return new ContentBlockStopEvent
                        {
                            Index = contentBlockIndex
                        };

                        contentBlockIndex++;
                    }
                }

                // Emit message_delta with stop reason on finish
                // Use anyFunctionCalls (across ALL chunks) — Gemini sends the function call
                // in one chunk and finishReason in a later chunk with empty text.
                if (candidate.FinishReason != null)
                {
                    var stopReason = anyFunctionCalls ? "tool_use" : MapFinishReason(candidate.FinishReason);

                    yield return new MessageDeltaEvent
                    {
                        Delta = new MessageDelta { StopReason = stopReason },
                        Usage = finalUsage
                    };
                }
            }

            // Fallback: if SSE parsing yielded nothing, try parsing the entire
            // response body as a single non-streaming GeminiResponse.
            if (!messageStarted && rawLines.Count > 0)
            {
                var fullBody = string.Join("", rawLines);
                GeminiResponse? fallback = null;
                try
                {
                    fallback = JsonSerializer.Deserialize<GeminiResponse>(fullBody, JsonReadOptions);
                }
                catch (JsonException)
                {
                    // Try joining with newlines in case of multiline JSON
                    try
                    {
                        fullBody = string.Join("\n", rawLines);
                        fallback = JsonSerializer.Deserialize<GeminiResponse>(fullBody, JsonReadOptions);
                    }
                    catch (JsonException)
                    {
                        // Cannot parse response at all
                    }
                }

                if (fallback?.Candidates?.FirstOrDefault()?.Content?.Parts != null)
                {
                    var converted = ConvertResponse(fallback);

                    yield return new MessageStartEvent
                    {
                        Message = new ClaudeResponse
                        {
                            Id = converted.Id,
                            Type = "message",
                            Role = "assistant",
                            Content = new List<ContentBlock>()
                        }
                    };

                    foreach (var block in converted.Content)
                    {
                        if (block is TextContentBlock textBlock)
                        {
                            yield return new ContentBlockStartEvent
                            {
                                Index = contentBlockIndex,
                                ContentBlock = new TextContentBlock { Text = "" }
                            };
                            yield return new ContentBlockDeltaEvent
                            {
                                Index = contentBlockIndex,
                                Delta = new TextDelta { Text = textBlock.Text }
                            };
                            yield return new ContentBlockStopEvent { Index = contentBlockIndex };
                            contentBlockIndex++;
                        }
                        else if (block is ToolUseBlock toolBlock)
                        {
                            yield return new ContentBlockStartEvent
                            {
                                Index = contentBlockIndex,
                                ContentBlock = toolBlock
                            };
                            var argsJson = toolBlock.Input.ValueKind != JsonValueKind.Undefined
                                ? toolBlock.Input.GetRawText() : "{}";
                            yield return new ContentBlockDeltaEvent
                            {
                                Index = contentBlockIndex,
                                Delta = new InputJsonDelta { PartialJson = argsJson }
                            };
                            yield return new ContentBlockStopEvent { Index = contentBlockIndex };
                            contentBlockIndex++;
                        }
                    }

                    var hasFuncCalls = converted.Content.OfType<ToolUseBlock>().Any();
                    yield return new MessageDeltaEvent
                    {
                        Delta = new MessageDelta
                        {
                            StopReason = hasFuncCalls ? "tool_use" : (converted.StopReason ?? "end_turn")
                        },
                        Usage = converted.Usage
                    };

                    finalUsage = converted.Usage;
                    messageStarted = true;
                }
            }

            if (messageStarted)
            {
                yield return new MessageStopEvent();
            }
        }
        finally
        {
            response?.Dispose();
            UnregisterCurrentRequest(linkedCts);

            if (finalUsage != null)
            {
                UsageTracker.Instance.RecordUsage(
                    finalUsage.InputTokens,
                    finalUsage.OutputTokens);

                StreamCompleted?.Invoke(this, finalUsage);
            }
        }
    }

    /// <inheritdoc/>
    public void CancelCurrentRequest()
    {
        lock (_ctsLock)
        {
            _currentRequestCts?.Cancel();
        }
    }

    // ──── Message Translation ────

    private GeminiRequest BuildGeminiRequest(
        string? systemPrompt,
        List<ClaudeMessage> messages,
        List<ToolDefinition>? tools,
        ApiSettings settings)
    {
        return new GeminiRequest
        {
            Contents = ConvertMessages(messages),
            SystemInstruction = !string.IsNullOrEmpty(systemPrompt)
                ? new GeminiContent
                {
                    Parts = new List<GeminiPart> { new() { Text = systemPrompt } }
                }
                : null,
            Tools = ConvertToolDefinitions(tools),
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = settings.Temperature,
                MaxOutputTokens = settings.MaxTokens
            }
        };
    }

    private List<GeminiContent> ConvertMessages(List<ClaudeMessage> messages)
    {
        var contents = new List<GeminiContent>();

        foreach (var message in messages)
        {
            var role = message.Role == "assistant" ? "model" : "user";
            var parts = ConvertContentToParts(message.Content);

            if (parts.Count > 0)
            {
                contents.Add(new GeminiContent
                {
                    Role = role,
                    Parts = parts
                });
            }
        }

        return contents;
    }

    private List<GeminiPart> ConvertContentToParts(object content)
    {
        var parts = new List<GeminiPart>();

        if (content is string text)
        {
            parts.Add(new GeminiPart { Text = text });
        }
        else if (content is JsonElement jsonElement)
        {
            ConvertJsonContentToParts(jsonElement, parts);
        }
        else if (content is List<ContentBlock> blocks)
        {
            foreach (var block in blocks)
            {
                ConvertContentBlockToPart(block, parts);
            }
        }
        else if (content is List<object> objectList)
        {
            // Handle ToolResult lists and mixed content
            foreach (var item in objectList)
            {
                if (item is ToolResultBlock toolResult)
                {
                    ConvertToolResultToPart(toolResult, parts);
                }
                else if (item is ContentBlock contentBlock)
                {
                    ConvertContentBlockToPart(contentBlock, parts);
                }
                else if (item is JsonElement jsonEl)
                {
                    ConvertJsonContentToParts(jsonEl, parts);
                }
            }
        }

        return parts;
    }

    private void ConvertJsonContentToParts(JsonElement element, List<GeminiPart> parts)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            parts.Add(new GeminiPart { Text = element.GetString() ?? "" });
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ConvertJsonElementBlockToPart(item, parts);
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            ConvertJsonElementBlockToPart(element, parts);
        }
    }

    private void ConvertJsonElementBlockToPart(JsonElement element, List<GeminiPart> parts)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("type", out var typeProp))
        {
            var type = typeProp.GetString();
            switch (type)
            {
                case "text":
                    if (element.TryGetProperty("text", out var textProp))
                    {
                        parts.Add(new GeminiPart { Text = textProp.GetString() ?? "" });
                    }
                    break;

                case "image":
                    if (element.TryGetProperty("source", out var sourceProp))
                    {
                        var mediaType = sourceProp.TryGetProperty("media_type", out var mt) ? mt.GetString() : "image/png";
                        var data = sourceProp.TryGetProperty("data", out var d) ? d.GetString() : "";
                        if (!string.IsNullOrEmpty(data))
                        {
                            parts.Add(new GeminiPart
                            {
                                InlineData = new GeminiInlineData
                                {
                                    MimeType = mediaType ?? "image/png",
                                    Data = data
                                }
                            });
                        }
                    }
                    break;

                case "tool_use":
                    if (element.TryGetProperty("name", out var nameProp) &&
                        element.TryGetProperty("id", out var idProp))
                    {
                        var name = nameProp.GetString() ?? "";
                        var id = idProp.GetString() ?? "";
                        var args = element.TryGetProperty("input", out var inputProp)
                            ? inputProp
                            : JsonDocument.Parse("{}").RootElement;

                        _toolCallIdToName[id] = name;
                        parts.Add(new GeminiPart
                        {
                            FunctionCall = new GeminiFunctionCall { Name = name, Args = args },
                            ThoughtSignature = _toolCallIdToThoughtSig.GetValueOrDefault(id)
                        });
                    }
                    break;

                case "tool_result":
                    if (element.TryGetProperty("tool_use_id", out var toolUseIdProp))
                    {
                        var toolUseId = toolUseIdProp.GetString() ?? "";
                        var funcName = _toolCallIdToName.GetValueOrDefault(toolUseId, "unknown");
                        var resultContent = element.TryGetProperty("content", out var contentProp)
                            ? contentProp.GetRawText()
                            : "\"\"";

                        parts.Add(new GeminiPart
                        {
                            FunctionResponse = new GeminiFunctionResponse
                            {
                                Name = funcName,
                                Response = JsonDocument.Parse($"{{\"result\":{resultContent}}}").RootElement
                            }
                        });

                        // If tool result contains an image, send it as a separate inlineData part
                        if (contentProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in contentProp.EnumerateArray())
                            {
                                if (item.TryGetProperty("type", out var itemType) &&
                                    itemType.GetString() == "image" &&
                                    item.TryGetProperty("source", out var imgSource))
                                {
                                    var imgData = imgSource.TryGetProperty("data", out var imgD) ? imgD.GetString() : "";
                                    var imgMime = imgSource.TryGetProperty("media_type", out var imgMt) ? imgMt.GetString() : "image/png";
                                    if (!string.IsNullOrEmpty(imgData))
                                    {
                                        parts.Add(new GeminiPart
                                        {
                                            InlineData = new GeminiInlineData
                                            {
                                                MimeType = imgMime ?? "image/png",
                                                Data = imgData
                                            }
                                        });
                                    }
                                }
                            }
                        }
                    }
                    break;
            }
        }
    }

    private void ConvertContentBlockToPart(ContentBlock block, List<GeminiPart> parts)
    {
        switch (block)
        {
            case TextContentBlock textBlock:
                parts.Add(new GeminiPart { Text = textBlock.Text });
                break;

            case ImageContentBlock imageBlock:
                parts.Add(new GeminiPart
                {
                    InlineData = new GeminiInlineData
                    {
                        MimeType = imageBlock.Source.MediaType,
                        Data = imageBlock.Source.Data
                    }
                });
                break;

            case ToolUseBlock toolUse:
                _toolCallIdToName[toolUse.Id] = toolUse.Name;
                parts.Add(new GeminiPart
                {
                    FunctionCall = new GeminiFunctionCall
                    {
                        Name = toolUse.Name,
                        Args = toolUse.Input
                    },
                    ThoughtSignature = _toolCallIdToThoughtSig.GetValueOrDefault(toolUse.Id)
                });
                break;

            case ToolResultBlock toolResult:
                ConvertToolResultToPart(toolResult, parts);
                break;
        }
    }

    private void ConvertToolResultToPart(ToolResultBlock toolResult, List<GeminiPart> parts)
    {
        var funcName = _toolCallIdToName.GetValueOrDefault(toolResult.ToolUseId, "unknown");

        // Build the response content
        JsonElement responseElement;
        if (toolResult.Content is string textContent)
        {
            responseElement = JsonDocument.Parse(
                JsonSerializer.Serialize(new { result = textContent }, JsonOptions)).RootElement;
        }
        else
        {
            var contentJson = JsonSerializer.Serialize(toolResult.Content, JsonOptions);
            responseElement = JsonDocument.Parse($"{{\"result\":{contentJson}}}").RootElement;
        }

        parts.Add(new GeminiPart
        {
            FunctionResponse = new GeminiFunctionResponse
            {
                Name = funcName,
                Response = responseElement
            }
        });

        // If tool result contains image content, send as separate inlineData part
        if (toolResult.Content is List<object> contentList)
        {
            foreach (var item in contentList)
            {
                // Anonymous types from ToolResultBlock.FromImage
                var json = JsonSerializer.Serialize(item, JsonOptions);
                var element = JsonDocument.Parse(json).RootElement;

                if (element.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "image" &&
                    element.TryGetProperty("source", out var source))
                {
                    var data = source.TryGetProperty("data", out var d) ? d.GetString() : "";
                    var mimeType = source.TryGetProperty("media_type", out var mt)
                        ? mt.GetString()
                        : (source.TryGetProperty("mediaType", out var mt2) ? mt2.GetString() : "image/png");

                    if (!string.IsNullOrEmpty(data))
                    {
                        parts.Add(new GeminiPart
                        {
                            InlineData = new GeminiInlineData
                            {
                                MimeType = mimeType ?? "image/png",
                                Data = data
                            }
                        });
                    }
                }
            }
        }
    }

    // ──── Tool Translation ────

    private static List<GeminiToolDeclaration>? ConvertToolDefinitions(List<ToolDefinition>? tools)
    {
        if (tools == null || tools.Count == 0)
            return null;

        var declarations = tools.Select(t => new GeminiFunctionDeclaration
        {
            Name = t.Name,
            Description = t.Description,
            Parameters = StripUnsupportedSchemaFields(t.InputSchema)
        }).ToList();

        return new List<GeminiToolDeclaration>
        {
            new() { FunctionDeclarations = declarations }
        };
    }

    /// <summary>
    /// Recursively removes JSON Schema fields that Gemini doesn't support
    /// (e.g., "additionalProperties", "$schema").
    /// </summary>
    private static JsonElement StripUnsupportedSchemaFields(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return schema;

        using var doc = JsonDocument.Parse(StripUnsupportedSchemaObject(schema));
        return doc.RootElement.Clone();
    }

    private static string StripUnsupportedSchemaObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return element.GetRawText();

        var stream = new System.IO.MemoryStream();
        using (var jsonWriter = new System.Text.Json.Utf8JsonWriter(stream))
        {
            jsonWriter.WriteStartObject();
            foreach (var property in element.EnumerateObject())
            {
                // Skip fields Gemini doesn't support in function declarations
                if (property.Name is "additionalProperties" or "$schema")
                    continue;

                jsonWriter.WritePropertyName(property.Name);

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    // Recurse into nested objects (e.g., "properties" values, "items")
                    var stripped = StripUnsupportedSchemaObject(property.Value);
                    using var nested = JsonDocument.Parse(stripped);
                    nested.RootElement.WriteTo(jsonWriter);
                }
                else if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    jsonWriter.WriteStartArray();
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            var stripped = StripUnsupportedSchemaObject(item);
                            using var nested = JsonDocument.Parse(stripped);
                            nested.RootElement.WriteTo(jsonWriter);
                        }
                        else
                        {
                            item.WriteTo(jsonWriter);
                        }
                    }
                    jsonWriter.WriteEndArray();
                }
                else
                {
                    property.Value.WriteTo(jsonWriter);
                }
            }
            jsonWriter.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    // ──── Response Translation ────

    private ClaudeResponse ConvertResponse(GeminiResponse geminiResponse)
    {
        var candidate = geminiResponse.Candidates?.FirstOrDefault();
        var contentBlocks = new List<ContentBlock>();
        var hasFunctionCalls = false;

        if (candidate?.Content?.Parts != null)
        {
            foreach (var part in candidate.Content.Parts)
            {
                if (part.Text != null)
                {
                    contentBlocks.Add(new TextContentBlock { Text = part.Text });
                }
                else if (part.FunctionCall != null)
                {
                    hasFunctionCalls = true;
                    var toolCallId = $"gemini_call_{Guid.NewGuid():N}";
                    _toolCallIdToName[toolCallId] = part.FunctionCall.Name;
                    if (part.ThoughtSignature != null)
                        _toolCallIdToThoughtSig[toolCallId] = part.ThoughtSignature;

                    contentBlocks.Add(new ToolUseBlock
                    {
                        Id = toolCallId,
                        Name = part.FunctionCall.Name,
                        Input = part.FunctionCall.Args.ValueKind != JsonValueKind.Undefined
                            ? part.FunctionCall.Args
                            : JsonDocument.Parse("{}").RootElement
                    });
                }
            }
        }

        var stopReason = hasFunctionCalls
            ? "tool_use"
            : MapFinishReason(candidate?.FinishReason);

        return new ClaudeResponse
        {
            Id = $"gemini_{Guid.NewGuid():N}",
            Type = "message",
            Role = "assistant",
            Content = contentBlocks,
            StopReason = stopReason,
            Usage = geminiResponse.UsageMetadata != null
                ? new Usage
                {
                    InputTokens = geminiResponse.UsageMetadata.PromptTokenCount,
                    OutputTokens = geminiResponse.UsageMetadata.CandidatesTokenCount
                }
                : null
        };
    }

    private static string MapFinishReason(string? finishReason) => finishReason switch
    {
        "STOP" => "end_turn",
        "MAX_TOKENS" => "max_tokens",
        "SAFETY" => "end_turn",
        "RECITATION" => "end_turn",
        _ => "end_turn"
    };

    // ──── Error Handling ────

    private static ClaudeApiException CreateApiException(System.Net.HttpStatusCode statusCode, string responseContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            var message = "Unknown error";
            if (root.TryGetProperty("error", out var errorObj) &&
                errorObj.TryGetProperty("message", out var msgProp))
            {
                message = msgProp.GetString() ?? message;
            }

            return statusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or (System.Net.HttpStatusCode)403 =>
                    new ClaudeApiException($"Authentication failed: {message}", "authentication_error", isRetryable: false),
                System.Net.HttpStatusCode.TooManyRequests =>
                    new ClaudeApiException($"Rate limit exceeded: {message}", "rate_limit_error", isRetryable: true),
                System.Net.HttpStatusCode.BadRequest =>
                    new ClaudeApiException($"Invalid request: {message}", "invalid_request_error", isRetryable: false),
                >= System.Net.HttpStatusCode.InternalServerError =>
                    new ClaudeApiException($"Server error: {message}", "server_error", isRetryable: true),
                _ =>
                    new ClaudeApiException($"API error ({statusCode}): {message}", "unknown", isRetryable: false)
            };
        }
        catch
        {
            return new ClaudeApiException($"API error ({statusCode}): {responseContent}");
        }
    }

    // ──── Infrastructure ────

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
