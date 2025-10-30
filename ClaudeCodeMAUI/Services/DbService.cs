using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MySqlConnector;
using ClaudeCodeMAUI.Models;
using Serilog;

namespace ClaudeCodeMAUI.Services
{
    /// <summary>
    /// Service for interacting with MariaDB database.
    /// Manages conversation session persistence and recovery.
    /// </summary>
    public class DbService
    {
        // ===== HARDCODED DATABASE CONFIGURATION =====
        private const string DB_HOST = "192.168.1.11";
        private const int DB_PORT = 3306;
        private const string DB_NAME = "ClaudeGui";

        private readonly string _connectionString;

        /// <summary>
        /// Constructor. Builds connection string from hardcoded values + User Secrets credentials.
        /// </summary>
        /// <param name="username">Database username from User Secrets</param>
        /// <param name="password">Database password from User Secrets</param>
        public DbService(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException("Database credentials (username/password) cannot be empty. " +
                                            "Please configure User Secrets.");
            }

            _connectionString = new MySqlConnectionStringBuilder
            {
                Server = DB_HOST,
                Port = (uint)DB_PORT,
                Database = DB_NAME,
                UserID = username,
                Password = password,
                CharacterSet = "utf8mb4",
                SslMode = MySqlSslMode.Preferred,
                ConnectionTimeout = 10, // 10 seconds timeout
                DefaultCommandTimeout = 30
            }.ConnectionString;

            Log.Information("DbService initialized with connection: {Host}:{Port}/{Database}",
                            DB_HOST, DB_PORT, DB_NAME);
        }

        /// <summary>
        /// Tests database connection. Should be called at app startup.
        /// If this fails, the app should not start.
        /// </summary>
        /// <returns>True if connection successful, false otherwise</returns>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();
                Log.Information("Database connection test successful");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Database connection test failed");
                return false;
            }
        }

        /// <summary>
        /// Inserts a new conversation session into the database.
        /// Called when first session_id is received from Claude.
        /// </summary>
        public async Task InsertSessionAsync(ConversationSession session)
        {
            const string sql = @"
                INSERT INTO conversations (session_id, tab_title, is_plan_mode, last_activity, status)
                VALUES (@SessionId, @TabTitle, @IsPlanMode, @LastActivity, @Status)";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionId", session.SessionId);
                command.Parameters.AddWithValue("@TabTitle", session.TabTitle);
                command.Parameters.AddWithValue("@IsPlanMode", session.IsPlanMode);
                command.Parameters.AddWithValue("@LastActivity", session.LastActivity);
                command.Parameters.AddWithValue("@Status", session.Status);

                await command.ExecuteNonQueryAsync();

                Log.Information("Inserted new session: {SessionId} - {TabTitle}", session.SessionId, session.TabTitle);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to insert session: {SessionId}", session.SessionId);
                throw;
            }
        }

        /// <summary>
        /// Updates the status of a conversation session.
        /// Used for marking as 'killed' or 'closed'.
        /// </summary>
        public async Task UpdateStatusAsync(string sessionId, string status)
        {
            const string sql = @"
                UPDATE conversations
                SET status = @Status, updated_at = NOW()
                WHERE session_id = @SessionId";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Status", status);
                command.Parameters.AddWithValue("@SessionId", sessionId);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    Log.Information("Updated session {SessionId} status to: {Status}", sessionId, status);
                }
                else
                {
                    Log.Warning("No session found with id: {SessionId}", sessionId);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update session status: {SessionId}", sessionId);
                // Don't throw - fire-and-forget operations should not block
            }
        }

        /// <summary>
        /// Updates the last_activity timestamp of a conversation session.
        /// Called periodically (e.g., every 5 minutes) to track activity.
        /// </summary>
        public async Task UpdateLastActivityAsync(string sessionId)
        {
            const string sql = @"
                UPDATE conversations
                SET last_activity = NOW(), updated_at = NOW()
                WHERE session_id = @SessionId";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionId", sessionId);

                await command.ExecuteNonQueryAsync();

                Log.Debug("Updated last_activity for session: {SessionId}", sessionId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update last_activity: {SessionId}", sessionId);
                // Don't throw - fire-and-forget
            }
        }

        /// <summary>
        /// Updates the plan mode flag for a session.
        /// </summary>
        public async Task UpdatePlanModeAsync(string sessionId, bool isPlanMode)
        {
            const string sql = @"
                UPDATE conversations
                SET is_plan_mode = @IsPlanMode, updated_at = NOW()
                WHERE session_id = @SessionId";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@IsPlanMode", isPlanMode);
                command.Parameters.AddWithValue("@SessionId", sessionId);

                await command.ExecuteNonQueryAsync();

                Log.Information("Updated plan mode for {SessionId}: {IsPlanMode}", sessionId, isPlanMode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update plan mode: {SessionId}", sessionId);
            }
        }

        /// <summary>
        /// Retrieves all active or killed conversation sessions for recovery.
        /// Called at app startup to recover sessions after crash/close.
        /// </summary>
        public async Task<List<ConversationSession>> GetActiveConversationsAsync()
        {
            const string sql = @"
                SELECT id, session_id, tab_title, is_plan_mode, last_activity, status, created_at, updated_at
                FROM conversations
                WHERE status IN ('active', 'killed')
                ORDER BY last_activity DESC";

            var sessions = new List<ConversationSession>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var tabTitleOrdinal = reader.GetOrdinal("tab_title");

                    var session = new ConversationSession
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("id")),
                        SessionId = reader.GetString(reader.GetOrdinal("session_id")),
                        TabTitle = reader.IsDBNull(tabTitleOrdinal)
                            ? "Untitled"
                            : reader.GetString(tabTitleOrdinal),
                        IsPlanMode = reader.GetBoolean(reader.GetOrdinal("is_plan_mode")),
                        LastActivity = reader.GetDateTime(reader.GetOrdinal("last_activity")),
                        Status = reader.GetString(reader.GetOrdinal("status")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                    };
                    sessions.Add(session);
                }

                Log.Information("Retrieved {Count} active/killed sessions for recovery", sessions.Count);
                return sessions;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to retrieve active conversations");
                throw;
            }
        }

        /// <summary>
        /// Batch updates multiple sessions to 'closed' status.
        /// Used when closing the application normally.
        /// </summary>
        public async Task UpdateStatusBatchAsync(List<string> sessionIds, string status)
        {
            if (sessionIds == null || sessionIds.Count == 0)
                return;

            // Build parameterized IN clause
            var parameters = new List<string>();
            for (int i = 0; i < sessionIds.Count; i++)
            {
                parameters.Add($"@SessionId{i}");
            }

            var sql = $@"
                UPDATE conversations
                SET status = @Status, updated_at = NOW()
                WHERE session_id IN ({string.Join(", ", parameters)})";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Status", status);

                for (int i = 0; i < sessionIds.Count; i++)
                {
                    command.Parameters.AddWithValue($"@SessionId{i}", sessionIds[i]);
                }

                var rowsAffected = await command.ExecuteNonQueryAsync();

                Log.Information("Batch updated {Count} sessions to status: {Status}", rowsAffected, status);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to batch update session status");
            }
        }
    }
}
