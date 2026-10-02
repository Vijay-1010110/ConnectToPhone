using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConnectToPhone.Mcp;

public interface IPhoneBridge
{
    Task<JsonObject> GetStatusAsync();
    Task<JsonArray> ListFilesAsync(string path);
    Task<JsonObject> DownloadFileAsync(string phonePath, string localPath);
    Task<JsonObject> UploadFileAsync(string localPath, string phonePath);
    Task<string> GetClipboardAsync();
    Task<bool> SetClipboardAsync(string text);
    Task<bool> ShowNotificationAsync(string title, string message);
}

public sealed class McpServer
{
    private readonly IPhoneBridge _bridge;

    public McpServer(IPhoneBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public async Task ProcessStdioAsync(CancellationToken ct = default)
    {
        using var reader = new StreamReader(Console.OpenStandardInput());
        using var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var request = JsonNode.Parse(line)?.AsObject();
                if (request == null) continue;

                var response = await HandleJsonRpcRequestAsync(request).ConfigureAwait(false);
                if (response != null)
                {
                    await writer.WriteLineAsync(response.ToJsonString().AsMemory(), ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                var errorResponse = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = null,
                    ["error"] = new JsonObject
                    {
                        ["code"] = -32603,
                        ["message"] = ex.Message
                    }
                };
                await writer.WriteLineAsync(errorResponse.ToJsonString().AsMemory(), ct).ConfigureAwait(false);
            }
        }
    }

    public async Task<JsonObject> HandleJsonRpcRequestAsync(JsonObject request)
    {
        var id = request["id"]?.DeepClone();
        string method = request["method"]?.GetValue<string>() ?? string.Empty;
        var parameters = request["params"]?.AsObject();

        var result = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id
        };

        switch (method)
        {
            case "initialize":
                result["result"] = new JsonObject
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = "ConnectToPhone MCP Server",
                        ["version"] = "1.0.0"
                    },
                    ["capabilities"] = new JsonObject
                    {
                        ["tools"] = new JsonObject()
                    }
                };
                break;

            case "tools/list":
                result["result"] = new JsonObject
                {
                    ["tools"] = new JsonArray
                    {
                        CreateToolDefinition("phone_get_status", "Returns connection health, active transports (Wi-Fi, USB, BT), battery %, and transfer speeds."),
                        CreateToolDefinition("phone_list_files", "Lists directories and files on the connected phone.",
                            new JsonObject { ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Remote directory path (or empty for root)" } }, ["path"]),
                        CreateToolDefinition("phone_download_file", "Pulls a file from phone storage directly to local PC disk.",
                            new JsonObject {
                                ["phone_path"] = new JsonObject { ["type"] = "string", ["description"] = "Path to file on phone" },
                                ["local_path"] = new JsonObject { ["type"] = "string", ["description"] = "Destination path on PC" }
                            }, ["phone_path", "local_path"]),
                        CreateToolDefinition("phone_upload_file", "Transmits a local file from PC to phone storage.",
                            new JsonObject {
                                ["local_path"] = new JsonObject { ["type"] = "string", ["description"] = "Source path on PC" },
                                ["phone_path"] = new JsonObject { ["type"] = "string", ["description"] = "Destination folder/file on phone" }
                            }, ["local_path", "phone_path"]),
                        CreateToolDefinition("phone_get_clipboard", "Fetches the current text clipboard content from the phone."),
                        CreateToolDefinition("phone_set_clipboard", "Injects text or URL directly into the phone's clipboard.",
                            new JsonObject { ["text"] = new JsonObject { ["type"] = "string", ["description"] = "Text to copy to phone" } }, ["text"]),
                        CreateToolDefinition("phone_show_notification", "Displays a push notification toast alert on the phone screen.",
                            new JsonObject {
                                ["title"] = new JsonObject { ["type"] = "string", ["description"] = "Notification title" },
                                ["message"] = new JsonObject { ["type"] = "string", ["description"] = "Notification message body" }
                            }, ["title", "message"])
                    }
                };
                break;

            case "tools/call":
                string toolName = parameters?["name"]?.GetValue<string>() ?? string.Empty;
                var arguments = parameters?["arguments"]?.AsObject() ?? [];
                var callResult = await ExecuteToolAsync(toolName, arguments).ConfigureAwait(false);
                result["result"] = new JsonObject
                {
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = callResult
                        }
                    }
                };
                break;

            default:
                result["error"] = new JsonObject
                {
                    ["code"] = -32601,
                    ["message"] = $"Method not found: {method}"
                };
                break;
        }

        return result;
    }

    private async Task<string> ExecuteToolAsync(string name, JsonObject args)
    {
        switch (name)
        {
            case "phone_get_status":
                return (await _bridge.GetStatusAsync().ConfigureAwait(false)).ToJsonString();

            case "phone_list_files":
                string path = args["path"]?.GetValue<string>() ?? "";
                return (await _bridge.ListFilesAsync(path).ConfigureAwait(false)).ToJsonString();

            case "phone_download_file":
                string pPath = args["phone_path"]?.GetValue<string>() ?? "";
                string lPath = args["local_path"]?.GetValue<string>() ?? "";
                return (await _bridge.DownloadFileAsync(pPath, lPath).ConfigureAwait(false)).ToJsonString();

            case "phone_upload_file":
                string src = args["local_path"]?.GetValue<string>() ?? "";
                string dest = args["phone_path"]?.GetValue<string>() ?? "";
                return (await _bridge.UploadFileAsync(src, dest).ConfigureAwait(false)).ToJsonString();

            case "phone_get_clipboard":
                return await _bridge.GetClipboardAsync().ConfigureAwait(false);

            case "phone_set_clipboard":
                string text = args["text"]?.GetValue<string>() ?? "";
                bool setOk = await _bridge.SetClipboardAsync(text).ConfigureAwait(false);
                return setOk ? "Clipboard updated successfully." : "Failed to set clipboard.";

            case "phone_show_notification":
                string title = args["title"]?.GetValue<string>() ?? "ConnectToPhone";
                string msg = args["message"]?.GetValue<string>() ?? "";
                bool notifOk = await _bridge.ShowNotificationAsync(title, msg).ConfigureAwait(false);
                return notifOk ? "Notification dispatched." : "Failed to dispatch notification.";

            default:
                return $"Unknown tool: {name}";
        }
    }

    private static JsonObject CreateToolDefinition(string name, string description, JsonObject? properties = null, string[]? required = null)
    {
        var inputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties ?? new JsonObject()
        };

        if (required != null && required.Length > 0)
        {
            var reqArr = new JsonArray();
            foreach (var r in required) reqArr.Add(r);
            inputSchema["required"] = reqArr;
        }

        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }
}
