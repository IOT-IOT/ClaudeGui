using System;
using System.Globalization;
using System.Text.RegularExpressions;
using ClaudeCodeMAUI.Models;
using Serilog;

namespace ClaudeCodeMAUI.Services
{
    /// <summary>
    /// Parser per l'output del comando /context di Claude.
    /// Estrae metriche e percentuali di utilizzo del contesto.
    /// </summary>
    public class ContextOutputParser
    {
        /// <summary>
        /// Esegue il parsing dell'output del comando /context.
        /// </summary>
        /// <param name="output">Output completo del comando</param>
        /// <returns>Oggetto ContextInfo con tutte le metriche estratte</returns>
        /// <exception cref="FormatException">Se l'output non è nel formato atteso</exception>
        public ContextInfo Parse(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                Log.Error("Output is empty or whitespace");
                throw new ArgumentException("Output is empty. The command may not have produced output in time. Try increasing the timeout or check if the session is valid.", nameof(output));
            }

            Log.Information("Parsing context output ({Length} chars)", output.Length);
            Log.Debug("Output preview (first 500 chars): {Preview}",
                output.Length > 500 ? output.Substring(0, 500) : output);

            var info = new ContextInfo
            {
                RawOutput = output
            };

            try
            {
                // Pattern per la riga modello in formato markdown: "**Model:** claude-sonnet-4-5-20250929"
                var modelPattern = @"\*\*Model:\*\*\s+(claude-[\w-]+)";
                var modelMatch = Regex.Match(output, modelPattern);

                if (modelMatch.Success)
                {
                    info.Model = modelMatch.Groups[1].Value;
                    Log.Information("Parsed model: {Model}", info.Model);
                }
                else
                {
                    Log.Warning("Could not parse model line");
                }

                // Pattern per la riga tokens: "**Tokens:** 171.0k / 200.0k (86%)"
                var tokensPattern = @"\*\*Tokens:\*\*\s+([\d.]+k?)\s*/\s*([\d.]+k?)\s*\(([\d.]+)%\)";
                var tokensMatch = Regex.Match(output, tokensPattern);

                if (tokensMatch.Success)
                {
                    info.UsedTokens = ParseTokenValue(tokensMatch.Groups[1].Value);
                    info.TotalTokens = ParseTokenValue(tokensMatch.Groups[2].Value);
                    info.UsagePercentage = double.Parse(tokensMatch.Groups[3].Value, CultureInfo.InvariantCulture);

                    Log.Information("Parsed tokens: {Used}/{Total} ({Percentage}%)",
                        info.UsedTokens, info.TotalTokens, info.UsagePercentage);
                }
                else
                {
                    Log.Warning("Could not parse tokens line");
                }

                // Pattern per le righe della tabella Categories in formato markdown
                // Formato: "| System prompt | 2.7k | 1.3% |"
                var detailPattern = @"\|\s*(System prompt|System tools|Memory files|Messages|Free space|Autocompact buffer)\s*\|\s*([\d.]+k?)\s*\|\s*([\d.]+)%\s*\|";
                var detailMatches = Regex.Matches(output, detailPattern, RegexOptions.IgnoreCase);

                foreach (Match match in detailMatches)
                {
                    var label = match.Groups[1].Value.Trim();
                    var tokens = ParseTokenValue(match.Groups[2].Value);
                    var percentage = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

                    Log.Debug("Parsed detail: {Label} = {Tokens} tokens ({Percentage}%)", label, tokens, percentage);

                    // Assegna i valori ai campi appropriati
                    switch (label.ToLowerInvariant())
                    {
                        case "system prompt":
                            info.SystemPromptTokens = tokens;
                            info.SystemPromptPercentage = percentage;
                            break;

                        case "system tools":
                            info.SystemToolsTokens = tokens;
                            info.SystemToolsPercentage = percentage;
                            break;

                        case "memory files":
                            info.MemoryFilesTokens = tokens;
                            info.MemoryFilesPercentage = percentage;
                            break;

                        case "messages":
                            info.MessagesTokens = tokens;
                            info.MessagesPercentage = percentage;
                            break;

                        case "free space":
                            info.FreeSpaceTokens = tokens;
                            info.FreeSpacePercentage = percentage;
                            break;

                        case "autocompact buffer":
                            info.AutocompactBufferTokens = tokens;
                            info.AutocompactBufferPercentage = percentage;
                            break;
                    }
                }

                Log.Information("Parsing completed successfully");
                return info;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse context output");
                throw new FormatException("Failed to parse context output. The format may have changed.", ex);
            }
        }

        /// <summary>
        /// Converte una stringa token in valore intero.
        /// Esempi: "2.7k" -> 2700, "174.9k" -> 174900, "864" -> 864
        /// IMPORTANTE: Usa InvariantCulture per gestire correttamente il punto decimale
        /// </summary>
        /// <param name="value">Valore da convertire (es. "2.7k", "174.9k", "864")</param>
        /// <returns>Valore intero in token</returns>
        private int ParseTokenValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            value = value.Trim().ToLowerInvariant();

            try
            {
                // Se termina con 'k', moltiplica per 1000
                if (value.EndsWith("k"))
                {
                    var numericPart = value.Substring(0, value.Length - 1);
                    // USA InvariantCulture per parsing corretto del punto decimale
                    var number = double.Parse(numericPart, CultureInfo.InvariantCulture);
                    return (int)(number * 1000);
                }
                else
                {
                    // Valore senza suffisso (es. "864")
                    return int.Parse(value, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse token value: {Value}", value);
                return 0;
            }
        }
    }
}
