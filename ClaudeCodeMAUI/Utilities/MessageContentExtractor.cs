using System;
using System.Text;
using System.Text.Json;
using Serilog;

namespace ClaudeCodeMAUI.Utilities
{
    /// <summary>
    /// Utility per estrarre il contenuto testuale dai messaggi JSON delle sessioni Claude Code.
    /// Preserva la formattazione Markdown originale presente nel JSON.
    /// </summary>
    public class MessageContentExtractor
    {
        /// <summary>
        /// Estrae il contenuto testuale da un messaggio JSON.
        /// Gestisce sia messaggi USER (content = string) che ASSISTANT (content = array).
        /// </summary>
        /// <param name="messageJson">JsonElement rappresentante un messaggio completo</param>
        /// <returns>Testo del messaggio in formato Markdown (come presente nel JSON)</returns>
        public static string ExtractContent(JsonElement messageJson)
        {
            try
            {
                // Verifica che esista il campo "message"
                if (!messageJson.TryGetProperty("message", out var message))
                {
                    Log.Warning("Message JSON does not contain 'message' property");
                    return "⚠️ No message content found";
                }

                // Verifica che esista il campo "content"
                if (!message.TryGetProperty("content", out var content))
                {
                    Log.Warning("Message does not contain 'content' property");
                    return "⚠️ No content found";
                }

                // Estrai il ruolo del messaggio (user o assistant)
                var role = message.TryGetProperty("role", out var roleElement)
                    ? roleElement.GetString()
                    : "unknown";

                var sb = new StringBuilder();
                sb.AppendLine($"### {GetRoleIcon(role)} {CapitalizeFirst(role)}");
                sb.AppendLine();

                // CASO 1: Content è una stringa semplice (tipico per messaggi USER)
                if (content.ValueKind == JsonValueKind.String)
                {
                    sb.AppendLine(content.GetString());
                }
                // CASO 2: Content è un array di oggetti (tipico per messaggi ASSISTANT)
                else if (content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in content.EnumerateArray())
                    {
                        if (!item.TryGetProperty("type", out var typeElement))
                        {
                            continue;
                        }

                        var type = typeElement.GetString();

                        if (type == "text")
                        {
                            // Estrai il testo Markdown così com'è (preserva grassetto, codice, ecc.)
                            if (item.TryGetProperty("text", out var textElement))
                            {
                                sb.AppendLine(textElement.GetString());
                                sb.AppendLine();
                            }
                        }
                        else if (type == "tool_use")
                        {
                            // Formatta la chiamata a un tool come blocco Markdown leggibile
                            sb.AppendLine(FormatToolUseAsMarkdown(item));
                            sb.AppendLine();
                        }
                        else if (type == "tool_result")
                        {
                            // Formatta il risultato di un tool
                            sb.AppendLine(FormatToolResultAsMarkdown(item));
                            sb.AppendLine();
                        }
                    }
                }
                else
                {
                    sb.AppendLine($"⚠️ Unknown content type: {content.ValueKind}");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to extract content from message JSON");
                return $"⚠️ Error extracting content: {ex.Message}";
            }
        }

        /// <summary>
        /// Formatta una chiamata a tool (tool_use) come blocco Markdown.
        /// Esempio: Tool Read con parametri file_path, offset, limit
        /// </summary>
        private static string FormatToolUseAsMarkdown(JsonElement toolUse)
        {
            var sb = new StringBuilder();

            try
            {
                var name = toolUse.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : "Unknown";

                var id = toolUse.TryGetProperty("id", out var idElement)
                    ? idElement.GetString()
                    : "";

                sb.AppendLine($"#### 🔧 Tool Call: **{name}**");
                sb.AppendLine();

                if (!string.IsNullOrEmpty(id))
                {
                    sb.AppendLine($"*ID: `{id}`*");
                    sb.AppendLine();
                }

                // Mostra i parametri del tool
                if (toolUse.TryGetProperty("input", out var input))
                {
                    sb.AppendLine("**Parameters:**");
                    sb.AppendLine("```json");
                    sb.AppendLine(JsonSerializer.Serialize(input, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
                    sb.AppendLine("```");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to format tool_use");
                sb.AppendLine("⚠️ Error formatting tool call");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formatta il risultato di un tool (tool_result) come blocco Markdown.
        /// Mostra il contenuto completo con syntax highlighting Markdown.
        /// </summary>
        private static string FormatToolResultAsMarkdown(JsonElement toolResult)
        {
            var sb = new StringBuilder();

            try
            {
                var toolUseId = toolResult.TryGetProperty("tool_use_id", out var idElement)
                    ? idElement.GetString()
                    : "unknown";

                sb.AppendLine($"#### ✅ Tool Result");
                sb.AppendLine();
                sb.AppendLine($"*Response to: `{toolUseId}`*");
                sb.AppendLine();

                if (toolResult.TryGetProperty("content", out var contentElement))
                {
                    var content = contentElement.GetString() ?? "";

                    // Mostra il contenuto completo senza limiti
                    // Usa syntax highlighting Markdown per rendere visibile la formattazione
                    sb.AppendLine("```markdown");
                    sb.AppendLine(content);
                    sb.AppendLine("```");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to format tool_result");
                sb.AppendLine("⚠️ Error formatting tool result");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Restituisce un'icona emoji per il ruolo del messaggio.
        /// </summary>
        private static string GetRoleIcon(string role)
        {
            return role?.ToLower() switch
            {
                "user" => "👤",
                "assistant" => "🤖",
                _ => "❓"
            };
        }

        /// <summary>
        /// Capitalizza la prima lettera di una stringa.
        /// </summary>
        private static string CapitalizeFirst(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return char.ToUpper(text[0]) + text.Substring(1);
        }
    }
}
