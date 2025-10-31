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
                INSERT INTO conversations (session_id, tab_title, last_activity, status)
                VALUES (@SessionId, @TabTitle,  @LastActivity, @Status)";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionId", session.SessionId);
                command.Parameters.AddWithValue("@TabTitle", session.TabTitle);
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
        /// Retrieves all active or killed conversation sessions for recovery.
        /// Called at app startup to recover sessions after crash/close.
        /// </summary>
        public async Task<List<ConversationSession>> GetActiveConversationsAsync()
        {
            const string sql = @"
                SELECT id, session_id, tab_title, last_activity, status, created_at, updated_at
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

        /// <summary>
        /// Salva un messaggio nella tabella messages.
        /// Chiamato ogni volta che viene inviato un messaggio utente o ricevuto un messaggio assistant.
        /// Il numero di sequenza viene calcolato automaticamente come MAX(sequence) + 1 per la conversazione.
        /// </summary>
        /// <param name="conversationId">ID della sessione di conversazione</param>
        /// <param name="role">Ruolo del mittente: "user" o "assistant"</param>
        /// <param name="content">Contenuto del messaggio (può includere markdown)</param>
        public async Task SaveMessageAsync(string conversationId, string role, string content)
        {
            // Prima ottieni il prossimo numero di sequenza per questa conversazione
            const string getSequenceSql = @"
                SELECT COALESCE(MAX(sequence), 0) + 1 AS next_sequence
                FROM messages
                WHERE conversation_id = @ConversationId";

            const string insertSql = @"
                INSERT INTO messages (conversation_id, role, content, timestamp, sequence)
                VALUES (@ConversationId, @Role, @Content, @Timestamp, @Sequence)";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // Ottieni il prossimo numero di sequenza
                int nextSequence;
                using (var getSeqCommand = new MySqlCommand(getSequenceSql, connection))
                {
                    getSeqCommand.Parameters.AddWithValue("@ConversationId", conversationId);
                    var result = await getSeqCommand.ExecuteScalarAsync();
                    nextSequence = Convert.ToInt32(result);
                }

                // Inserisci il messaggio con il numero di sequenza calcolato
                using (var insertCommand = new MySqlCommand(insertSql, connection))
                {
                    insertCommand.Parameters.AddWithValue("@ConversationId", conversationId);
                    insertCommand.Parameters.AddWithValue("@Role", role);
                    insertCommand.Parameters.AddWithValue("@Content", content);
                    insertCommand.Parameters.AddWithValue("@Timestamp", DateTime.UtcNow);
                    insertCommand.Parameters.AddWithValue("@Sequence", nextSequence);

                    await insertCommand.ExecuteNonQueryAsync();

                    Log.Debug("Saved message for session {SessionId}: role={Role}, sequence={Sequence}, length={Length}",
                             conversationId, role, nextSequence, content.Length);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save message for session: {SessionId}", conversationId);
                // Non fare throw - il salvataggio dei messaggi è opzionale e non deve bloccare l'app
            }
        }

        /// <summary>
        /// Recupera gli ultimi N messaggi di una conversazione in ordine cronologico.
        /// Usato per visualizzare la storia quando si riprende una sessione.
        /// </summary>
        /// <param name="conversationId">ID della sessione di conversazione</param>
        /// <param name="count">Numero massimo di messaggi da recuperare (default 10)</param>
        /// <returns>Lista di messaggi ordinati cronologicamente (dal più vecchio al più recente)</returns>
        public async Task<List<ConversationMessage>> GetLastMessagesAsync(string conversationId, int count = 10)
        {
            if (count <= 0)
            {
                Log.Debug("GetLastMessagesAsync called with count={Count}, returning empty list", count);
                return new List<ConversationMessage>();
            }

            // Usa una subquery per prendere gli ultimi N messaggi in ordine decrescente,
            // poi riordina il risultato in ordine crescente per visualizzazione cronologica
            const string sql = @"
                SELECT id, conversation_id, role, content, timestamp, sequence
                FROM (
                    SELECT id, conversation_id, role, content, timestamp, sequence
                    FROM messages
                    WHERE conversation_id = @ConversationId
                    ORDER BY sequence DESC
                    LIMIT @Count
                ) AS last_messages
                ORDER BY sequence ASC";

            var messages = new List<ConversationMessage>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@ConversationId", conversationId);
                command.Parameters.AddWithValue("@Count", count);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var message = new ConversationMessage
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("id")),
                        ConversationId = reader.GetString(reader.GetOrdinal("conversation_id")),
                        Role = reader.GetString(reader.GetOrdinal("role")),
                        Content = reader.GetString(reader.GetOrdinal("content")),
                        Timestamp = reader.GetDateTime(reader.GetOrdinal("timestamp")),
                        Sequence = reader.GetInt32(reader.GetOrdinal("sequence"))
                    };
                    messages.Add(message);
                }

                Log.Information("Retrieved {Count} historical messages for session: {SessionId}", messages.Count, conversationId);
                return messages;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to retrieve messages for session: {SessionId}", conversationId);
                return new List<ConversationMessage>(); // Ritorna lista vuota in caso di errore
            }
        }
    }
}
