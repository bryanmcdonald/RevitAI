using System.Text.Json;
using Autodesk.Revit.UI;

namespace RevitAI.Tools;

/// <summary>
/// A simple test tool that echoes back a message.
/// Used to verify the tool framework is working correctly.
/// </summary>
public sealed class EchoTool : IRevitTool
{
    private static readonly JsonElement _inputSchema;

    static EchoTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "message": {
                        "type": "string",
                        "description": "The message to echo back"
                    }
                },
                "required": ["message"]
            }
            """;
        _inputSchema = JsonDocument.Parse(schemaJson).RootElement.Clone();
    }

    public string Name => "echo";

    public string Description => "Echoes back a message. Use this tool to test that the tool framework is working correctly.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!input.TryGetProperty("message", out var messageElement))
        {
            return Task.FromResult(ToolResult.Error("Missing required parameter: message"));
        }

        var message = messageElement.GetString();

        if (string.IsNullOrEmpty(message))
        {
            return Task.FromResult(ToolResult.Error("Parameter 'message' cannot be empty"));
        }

        return Task.FromResult(ToolResult.Ok($"Echo: {message}"));
    }
}
