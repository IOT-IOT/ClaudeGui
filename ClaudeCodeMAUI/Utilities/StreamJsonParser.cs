using System;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ClaudeCodeMAUI.Utilities
{
    /// <summary>
    /// Event args for session initialization (first system message with session_id)
    /// </summary>
    public class SessionInitializedEventArgs : EventArgs
    {
        public string SessionId { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string PermissionMode { get; set; } = string.Empty;
    }

    /// <summary>
    /// Event args for tool call from Claude
    /// </summary>
    public class ToolCallEventArgs : EventArgs
    {
        public string ToolName { get; set; } = string.Empty;
        public string ToolId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public JObject Input { get; set; } = new JObject();
    }

    /// <summary>
    /// Event args for text content from Claude
    /// </summary>
    public class TextReceivedEventArgs : EventArgs
    {
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// Event args for tool result
    /// </summary>
    public class ToolResultEventArgs : EventArgs
    {
        public string ToolUseId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsError { get; set; }
    }

    /// <summary>
    /// Event args for result metadata (final message with costs, tokens, etc.)
    /// </summary>
    public class MetadataEventArgs : EventArgs
    {
        public bool IsSuccess { get; set; }
        public long DurationMs { get; set; }
        public long DurationApiMs { get; set; }
        public int NumTurns { get; set; }
        public decimal TotalCostUsd { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int CacheReadTokens { get; set; }
        public int CacheCreationTokens { get; set; }
        public string Model { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parses JSONL stream from Claude Code headless mode.
    /// Emits typed events for different message types.
    /// </summary>
    public class StreamJsonParser
    {
        // Events
        public event EventHandler<SessionInitializedEventArgs>? SessionInitialized;
        public event EventHandler<ToolCallEventArgs>? ToolCallReceived;
        public event EventHandler<TextReceivedEventArgs>? TextReceived;
        public event EventHandler<ToolResultEventArgs>? ToolResultReceived;
        public event EventHandler<MetadataEventArgs>? MetadataReceived;

        /// <summary>
        /// Parses a single JSONL line and raises appropriate events
        /// </summary>
        public void ParseLine(string jsonLine)
        {
            try
            {
                var json = JObject.Parse(jsonLine);
                var type = json["type"]?.ToString();

                // DEBUG: Log ogni tipo di messaggio ricevuto
                Log.Debug("Received message type: {Type}", type);

                switch (type)
                {
                    case "system":
                        HandleSystemMessage(json);
                        break;

                    case "assistant":
                        HandleAssistantMessage(json);
                        break;

                    case "user":
                        HandleUserMessage(json);
                        break;

                    case "result":
                        HandleResultMessage(json);
                        break;

                    case "stream_event":
                        // Ignore partial stream events for now (we use complete messages)
                        Log.Debug("Ignoring stream_event");
                        break;

                    default:
                        Log.Debug("Unknown message type: {Type}", type);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse JSON line: {Line}", jsonLine.Length > 100 ? jsonLine.Substring(0, 100) + "..." : jsonLine);
            }
        }

        /// <summary>
        /// Handles "system" messages (init with session_id)
        /// </summary>
        private void HandleSystemMessage(JObject json)
        {
            var subtype = json["subtype"]?.ToString();

            if (subtype == "init")
            {
                var sessionId = json["session_id"]?.ToString() ?? string.Empty;
                var model = json["model"]?.ToString() ?? string.Empty;
                var permissionMode = json["permissionMode"]?.ToString() ?? "default";

                Log.Information("Session initialized: {SessionId}, Model: {Model}, PermissionMode: {PermissionMode}",
                                sessionId, model, permissionMode);

                SessionInitialized?.Invoke(this, new SessionInitializedEventArgs
                {
                    SessionId = sessionId,
                    Model = model,
                    PermissionMode = permissionMode
                });
            }
        }

        /// <summary>
        /// Handles "assistant" messages (text or tool_use)
        /// </summary>
        private void HandleAssistantMessage(JObject json)
        {
            Log.Information("HandleAssistantMessage called");

            var message = json["message"] as JObject;
            if (message == null)
            {
                Log.Warning("No 'message' field in assistant message");
                return;
            }

            var content = message["content"] as JArray;
            if (content == null)
            {
                Log.Warning("No 'content' array in assistant message");
                return;
            }

            Log.Information("Processing {Count} content blocks", content.Count);

            // Process each content block
            foreach (var block in content)
            {
                var blockType = block["type"]?.ToString();
                Log.Information("Content block type: {BlockType}", blockType);

                if (blockType == "text")
                {
                    var text = block["text"]?.ToString() ?? string.Empty;
                    Log.Information("Text block length: {Length}", text.Length);

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        Log.Information("Firing TextReceived event with {Length} chars", text.Length);
                        TextReceived?.Invoke(this, new TextReceivedEventArgs { Text = text });
                    }
                }
                else if (blockType == "tool_use")
                {
                    var toolName = block["name"]?.ToString() ?? string.Empty;
                    var toolId = block["id"]?.ToString() ?? string.Empty;
                    var input = block["input"] as JObject ?? new JObject();

                    // Try to extract description from input (common for many tools)
                    var description = input["description"]?.ToString() ?? string.Empty;

                    Log.Debug("Tool call: {ToolName} - {Description}", toolName, description);

                    ToolCallReceived?.Invoke(this, new ToolCallEventArgs
                    {
                        ToolName = toolName,
                        ToolId = toolId,
                        Description = description,
                        Input = input
                    });
                }
            }
        }

        /// <summary>
        /// Handles "user" messages (tool results)
        /// </summary>
        private void HandleUserMessage(JObject json)
        {
            var message = json["message"] as JObject;
            if (message == null)
                return;

            var content = message["content"] as JArray;
            if (content == null)
                return;

            // Process tool results
            foreach (var block in content)
            {
                var blockType = block["type"]?.ToString();

                if (blockType == "tool_result")
                {
                    var toolUseId = block["tool_use_id"]?.ToString() ?? string.Empty;
                    var resultContent = block["content"]?.ToString() ?? string.Empty;
                    var isError = block["is_error"]?.ToObject<bool>() ?? false;

                    Log.Debug("Tool result: {ToolUseId}, IsError: {IsError}, Length: {Length}",
                              toolUseId, isError, resultContent.Length);

                    ToolResultReceived?.Invoke(this, new ToolResultEventArgs
                    {
                        ToolUseId = toolUseId,
                        Content = resultContent,
                        IsError = isError
                    });
                }
            }
        }

        /// <summary>
        /// Handles "result" messages (final metadata with costs, tokens, etc.)
        /// </summary>
        private void HandleResultMessage(JObject json)
        {
            var subtype = json["subtype"]?.ToString();
            var isSuccess = subtype == "success";

            var durationMs = json["duration_ms"]?.ToObject<long>() ?? 0;
            var durationApiMs = json["duration_api_ms"]?.ToObject<long>() ?? 0;
            var numTurns = json["num_turns"]?.ToObject<int>() ?? 0;
            var totalCostUsd = json["total_cost_usd"]?.ToObject<decimal>() ?? 0m;

            // Parse usage
            var usage = json["usage"] as JObject;
            var inputTokens = usage?["input_tokens"]?.ToObject<int>() ?? 0;
            var outputTokens = usage?["output_tokens"]?.ToObject<int>() ?? 0;
            var cacheReadTokens = usage?["cache_read_input_tokens"]?.ToObject<int>() ?? 0;
            var cacheCreationTokens = usage?["cache_creation_input_tokens"]?.ToObject<int>() ?? 0;

            // Try to get model from modelUsage (first key)
            var modelUsage = json["modelUsage"] as JObject;
            var model = string.Empty;
            if (modelUsage != null && modelUsage.Count > 0)
            {
                model = modelUsage.Properties().First().Name;
            }

            Log.Information("Result: Success={IsSuccess}, Duration={DurationMs}ms, Cost=${Cost}, Turns={Turns}",
                            isSuccess, durationMs, totalCostUsd, numTurns);

            MetadataReceived?.Invoke(this, new MetadataEventArgs
            {
                IsSuccess = isSuccess,
                DurationMs = durationMs,
                DurationApiMs = durationApiMs,
                NumTurns = numTurns,
                TotalCostUsd = totalCostUsd,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CacheReadTokens = cacheReadTokens,
                CacheCreationTokens = cacheCreationTokens,
                Model = model
            });
        }
    }
}
