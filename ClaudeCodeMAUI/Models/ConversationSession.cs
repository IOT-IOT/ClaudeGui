using System;
using System.Collections.Generic;

namespace ClaudeCodeMAUI.Models
{
    /// <summary>
    /// Represents a Claude Code conversation session.
    /// Combines database-persisted data with runtime metadata.
    /// </summary>
    public class ConversationSession
    {
        // ===== Database Fields =====

        /// <summary>
        /// Database primary key (nullable for new sessions not yet persisted)
        /// </summary>
        public int? Id { get; set; }

        /// <summary>
        /// Claude session UUID (from stream-json "system" init message)
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// User-friendly tab title (first 30 chars of initial prompt)
        /// </summary>
        public string TabTitle { get; set; } = "New Conversation";

        /// <summary>
        /// Last activity timestamp (updated periodically)
        /// </summary>
        public DateTime LastActivity { get; set; } = DateTime.Now;

        /// <summary>
        /// Session status: active, closed, killed
        /// </summary>
        public string Status { get; set; } = "active";

        /// <summary>
        /// Session creation timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Last update timestamp
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;


        // ===== Runtime Metadata (NOT in database) =====

        /// <summary>
        /// Total cost in USD accumulated during this session
        /// </summary>
        public decimal TotalCostUsd { get; set; } = 0;

        /// <summary>
        /// Total input tokens used
        /// </summary>
        public int TotalInputTokens { get; set; } = 0;

        /// <summary>
        /// Total output tokens generated
        /// </summary>
        public int TotalOutputTokens { get; set; } = 0;

        /// <summary>
        /// Total cache read tokens
        /// </summary>
        public int TotalCacheReadTokens { get; set; } = 0;

        /// <summary>
        /// Total cache creation tokens
        /// </summary>
        public int TotalCacheCreationTokens { get; set; } = 0;

        /// <summary>
        /// Number of conversation turns (increments by Claude)
        /// </summary>
        public int NumTurns { get; set; } = 0;

        /// <summary>
        /// List of tools used during this session (Bash, Read, Edit, etc.)
        /// Key: tool name, Value: usage count
        /// </summary>
        public Dictionary<string, int> ToolsUsed { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Current model being used (e.g., claude-sonnet-4-5-20250929)
        /// </summary>
        public string CurrentModel { get; set; } = string.Empty;

        /// <summary>
        /// Total duration in milliseconds (sum of all response durations)
        /// </summary>
        public long TotalDurationMs { get; set; } = 0;

        /// <summary>
        /// Last response duration in milliseconds
        /// </summary>
        public long LastDurationMs { get; set; } = 0;


        // ===== Helper Methods =====

        /// <summary>
        /// Updates runtime metadata from a Claude "result" message
        /// </summary>
        public void UpdateFromResult(decimal costUsd, int inputTokens, int outputTokens,
                                      int cacheReadTokens, int cacheCreationTokens,
                                      int numTurns, long durationMs, string model)
        {
            TotalCostUsd += costUsd;
            TotalInputTokens += inputTokens;
            TotalOutputTokens += outputTokens;
            TotalCacheReadTokens += cacheReadTokens;
            TotalCacheCreationTokens += cacheCreationTokens;
            NumTurns = numTurns;
            TotalDurationMs += durationMs;
            LastDurationMs = durationMs;
            CurrentModel = model;
            LastActivity = DateTime.Now;
        }

        /// <summary>
        /// Records a tool usage
        /// </summary>
        public void RecordToolUsage(string toolName)
        {
            if (ToolsUsed.ContainsKey(toolName))
            {
                ToolsUsed[toolName]++;
            }
            else
            {
                ToolsUsed[toolName] = 1;
            }
        }

        /// <summary>
        /// Returns a formatted string of tools used
        /// Example: "Bash (3x), Read (5x), Edit (2x)"
        /// </summary>
        public string GetToolsSummary()
        {
            if (ToolsUsed.Count == 0)
                return "None";

            var tools = new List<string>();
            foreach (var kvp in ToolsUsed)
            {
                tools.Add($"{kvp.Key} ({kvp.Value}x)");
            }
            return string.Join(", ", tools);
        }

        /// <summary>
        /// Generates tab title from first prompt (max 30 chars)
        /// </summary>
        public static string GenerateTabTitle(string firstPrompt)
        {
            if (string.IsNullOrWhiteSpace(firstPrompt))
                return "New Conversation";

            var title = firstPrompt.Trim();
            if (title.Length > 30)
                title = title.Substring(0, 27) + "...";

            return title;
        }
    }
}
