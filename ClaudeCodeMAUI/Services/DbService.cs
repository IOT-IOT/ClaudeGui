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
                INSERT INTO conversations (session_id, tab_title, last_activity, status, working_directory)
                VALUES (@SessionId, @TabTitle,  @LastActivity, @Status, @WorkingDirectory)";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionId", session.SessionId);
                command.Parameters.AddWithValue("@TabTitle", session.TabTitle);
                command.Parameters.AddWithValue("@LastActivity", session.LastActivity);
                command.Parameters.AddWithValue("@Status", session.Status);
                command.Parameters.AddWithValue("@WorkingDirectory", session.WorkingDirectory);

                await command.ExecuteNonQueryAsync();

                Log.Information("Inserted new session: {SessionId} - {TabTitle} - WorkingDir: {WorkingDir}",
                    session.SessionId, session.TabTitle, session.WorkingDirectory);
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
        /// Updates the working directory of a conversation session.
        /// Used if user wants to change the working directory during a session.
        /// </summary>
        public async Task UpdateWorkingDirectoryAsync(string sessionId, string workingDirectory)
        {
            const string sql = @"
                UPDATE conversations
                SET working_directory = @WorkingDirectory, updated_at = NOW()
                WHERE session_id = @SessionId";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@WorkingDirectory", workingDirectory);
                command.Parameters.AddWithValue("@SessionId", sessionId);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    Log.Information("Updated working directory for session {SessionId} to: {WorkingDir}",
                                    sessionId, workingDirectory);
                }
                else
                {
                    Log.Warning("No session found with id: {SessionId}", sessionId);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update working directory for session: {SessionId}", sessionId);
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
                SELECT id, session_id, tab_title, last_activity, status, created_at, updated_at, working_directory
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
                    var workingDirOrdinal = reader.GetOrdinal("working_directory");

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
                        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
                        WorkingDirectory = reader.IsDBNull(workingDirOrdinal)
                            ? @"C:\Sources\ClaudeGui"
                            : reader.GetString(workingDirOrdinal)
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
        /// Salva un messaggio nella tabella messages (versione semplificata per retrocompatibilità).
        /// </summary>
        /// <param name="conversationId">ID della sessione di conversazione</param>
        /// <param name="role">Ruolo del mittente: "user" o "assistant"</param>
        /// <param name="content">Contenuto del messaggio (può includere markdown)</param>
        public async Task SaveMessageAsync(string conversationId, string role, string content)
        {
            await SaveMessageAsync(conversationId, role, content, null, null);
        }

        /// <summary>
        /// Salva un messaggio nel database con metadati completi dal file .jsonl
        /// Il numero di sequenza viene calcolato automaticamente come MAX(sequence) + 1 per la conversazione.
        /// </summary>
        public async Task SaveMessageAsync(
            string conversationId,
            string role,
            string content,
            DateTime? timestamp = null,
            string? uuid = null,
            string? parentUuid = null,
            string? version = null,
            string? gitBranch = null,
            bool? isSidechain = null,
            string? userType = null,
            string? cwd = null,
            string? requestId = null,
            string? model = null,
            string? usageJson = null,
            string? messageType = null)
        {
            // Prima ottieni il prossimo numero di sequenza per questa conversazione
            const string getSequenceSql = @"
                SELECT COALESCE(MAX(sequence), 0) + 1 AS next_sequence
                FROM messages
                WHERE conversation_id = @ConversationId";

            const string insertSql = @"
                INSERT INTO messages (
                    conversation_id, role, content, timestamp, sequence,
                    uuid, parent_uuid, version, git_branch, is_sidechain,
                    user_type, cwd, request_id, model, usage_json, message_type
                )
                VALUES (
                    @ConversationId, @Role, @Content, @Timestamp, @Sequence,
                    @Uuid, @ParentUuid, @Version, @GitBranch, @IsSidechain,
                    @UserType, @Cwd, @RequestId, @Model, @UsageJson, @MessageType
                )
                ON DUPLICATE KEY UPDATE uuid = uuid";  // Ignora duplicati per uuid

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

                // Inserisci il messaggio con tutti i metadati
                using (var insertCommand = new MySqlCommand(insertSql, connection))
                {
                    insertCommand.Parameters.AddWithValue("@ConversationId", conversationId);
                    insertCommand.Parameters.AddWithValue("@Role", role);
                    insertCommand.Parameters.AddWithValue("@Content", content);
                    insertCommand.Parameters.AddWithValue("@Timestamp", timestamp ?? DateTime.UtcNow);
                    insertCommand.Parameters.AddWithValue("@Sequence", nextSequence);
                    insertCommand.Parameters.AddWithValue("@Uuid", uuid ?? (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@ParentUuid", parentUuid ?? (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@Version", version ?? (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@GitBranch", gitBranch ?? (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@IsSidechain", isSidechain ?? (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@UserType", userType ?? (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@Cwd", cwd ?? (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@RequestId", requestId ?? (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@Model", model ?? (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@UsageJson", usageJson ?? (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@MessageType", messageType ?? role);

                    await insertCommand.ExecuteNonQueryAsync();

                    Log.Debug("Saved message: session={SessionId}, type={Type}, uuid={Uuid}, seq={Seq}",
                             conversationId, messageType ?? role, uuid, nextSequence);
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

        // ===== NUOVI METODI PER TABELLA SESSIONS (Multi-Sessione con Tab) =====

        /// <summary>
        /// Rappresenta un record dalla tabella Sessions
        /// </summary>
        public class SessionDbRow
        {
            public int Id { get; set; }
            public string SessionId { get; set; } = string.Empty;
            public string? Name { get; set; }
            public string WorkingDirectory { get; set; } = string.Empty;
            public string Status { get; set; } = "open";
            public DateTime CreatedAt { get; set; }
            public DateTime LastActivity { get; set; }
            public bool Processed { get; set; } = false;
            public bool Excluded { get; set; } = false;
            public string? ExcludedReason { get; set; } = null;
        }

        /// <summary>
        /// Ottiene una sessione dal DB per session_id
        /// </summary>
        public async Task<SessionDbRow?> GetSessionByIdAsync(string sessionId)
        {
            const string sql = "SELECT * FROM Sessions WHERE session_id = @SessionId";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionId", sessionId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new SessionDbRow
                    {
                        Id = reader.GetInt32("id"),
                        SessionId = reader.GetString("session_id"),
                        Name = reader.IsDBNull(reader.GetOrdinal("name")) ? null : reader.GetString("name"),
                        WorkingDirectory = reader.GetString("working_directory"),
                        Status = reader.GetString("status"),
                        CreatedAt = reader.GetDateTime("created_at"),
                        LastActivity = reader.GetDateTime("last_activity"),
                        Processed = reader.GetBoolean("processed"),
                        Excluded = reader.GetBoolean("excluded"),
                        ExcludedReason = reader.IsDBNull(reader.GetOrdinal("excluded_reason")) ? null : reader.GetString("excluded_reason")
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get session by ID: {SessionId}", sessionId);
                return null;
            }
        }

        /// <summary>
        /// Ottiene tutte le sessioni aperte (status = 'open')
        /// </summary>
        public async Task<List<SessionDbRow>> GetOpenSessionsAsync()
        {
            const string sql = "SELECT * FROM Sessions WHERE status = 'open' ORDER BY last_activity DESC";

            var sessions = new List<SessionDbRow>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    sessions.Add(new SessionDbRow
                    {
                        Id = reader.GetInt32("id"),
                        SessionId = reader.GetString("session_id"),
                        Name = reader.IsDBNull(reader.GetOrdinal("name")) ? null : reader.GetString("name"),
                        WorkingDirectory = reader.GetString("working_directory"),
                        Status = reader.GetString("status"),
                        CreatedAt = reader.GetDateTime("created_at"),
                        LastActivity = reader.GetDateTime("last_activity"),
                        Processed = reader.GetBoolean("processed"),
                        Excluded = reader.GetBoolean("excluded"),
                        ExcludedReason = reader.IsDBNull(reader.GetOrdinal("excluded_reason")) ? null : reader.GetString("excluded_reason")
                    });
                }

                Log.Information("Retrieved {Count} open sessions", sessions.Count);
                return sessions;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get open sessions");
                return sessions;
            }
        }

        /// <summary>
        /// Ottiene tutte le sessioni chiuse (status = 'closed') non escluse.
        /// Le sessioni escluse (excluded = TRUE) non vengono restituite.
        /// </summary>
        public async Task<List<SessionDbRow>> GetClosedSessionsAsync()
        {
            const string sql = "SELECT * FROM Sessions WHERE status = 'closed' AND excluded = FALSE ORDER BY last_activity DESC";

            var sessions = new List<SessionDbRow>();

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    sessions.Add(new SessionDbRow
                    {
                        Id = reader.GetInt32("id"),
                        SessionId = reader.GetString("session_id"),
                        Name = reader.IsDBNull(reader.GetOrdinal("name")) ? null : reader.GetString("name"),
                        WorkingDirectory = reader.GetString("working_directory"),
                        Status = reader.GetString("status"),
                        CreatedAt = reader.GetDateTime("created_at"),
                        LastActivity = reader.GetDateTime("last_activity"),
                        Processed = reader.GetBoolean("processed"),
                        Excluded = reader.GetBoolean("excluded"),
                        ExcludedReason = reader.IsDBNull(reader.GetOrdinal("excluded_reason")) ? null : reader.GetString("excluded_reason")
                    });
                }

                Log.Information("Retrieved {Count} closed non-excluded sessions", sessions.Count);
                return sessions;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get closed sessions");
                return sessions;
            }
        }

        /// <summary>
        /// Marca una sessione come processata e imposta il flag excluded con il motivo.
        /// Chiamato dopo aver verificato se una sessione è di tipo "summary" o "file-history-snapshot".
        /// </summary>
        /// <param name="sessionId">UUID della sessione</param>
        /// <param name="isExcluded">True se la sessione deve essere esclusa</param>
        /// <param name="excludedReason">Motivo dell'esclusione (es: "summary", "file-history-snapshot"), null se non esclusa</param>
        public async Task MarkSessionAsProcessedAsync(string sessionId, bool isExcluded, string? excludedReason = null)
        {
            const string sql = @"
                UPDATE Sessions
                SET processed = TRUE, excluded = @IsExcluded, excluded_reason = @ExcludedReason
                WHERE session_id = @SessionId";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionId", sessionId);
                command.Parameters.AddWithValue("@IsExcluded", isExcluded);
                command.Parameters.AddWithValue("@ExcludedReason", excludedReason ?? (object)DBNull.Value);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    Log.Debug("Marked session as processed: {SessionId}, Excluded: {IsExcluded}, Reason: {Reason}",
                             sessionId, isExcluded, excludedReason ?? "N/A");
                }
                else
                {
                    Log.Warning("Failed to mark session as processed (not found): {SessionId}", sessionId);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to mark session as processed: {SessionId}", sessionId);
            }
        }

        /// <summary>
        /// Resetta il flag processed di tutte le sessioni a FALSE.
        /// Utilizzato per forzare una riscansione completa del filesystem preservando i nomi assegnati.
        /// I file verranno riprocessati alla prossima scansione.
        /// </summary>
        public async Task ResetProcessedFlagAsync()
        {
            const string sql = "UPDATE Sessions SET processed = FALSE";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                var rowsAffected = await command.ExecuteNonQueryAsync();

                Log.Information("Reset processed flag for {Count} sessions", rowsAffected);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to reset processed flag");
                throw;
            }
        }

        /// <summary>
        /// Rimuove dal database tutte le sessioni che non sono nella lista fornita (garbage collection).
        /// Utilizzato per sincronizzare il database con il filesystem: rimuove sessioni orfane
        /// (file .jsonl cancellati ma record ancora nel DB).
        /// Il filesystem è la single source of truth.
        /// </summary>
        /// <param name="validSessionIds">Lista di session_id validi trovati nel filesystem</param>
        /// <returns>Numero di sessioni rimosse</returns>
        public async Task<int> RemoveOrphanedSessionsAsync(List<string> validSessionIds)
        {
            if (validSessionIds == null || validSessionIds.Count == 0)
            {
                Log.Warning("No valid session IDs provided to RemoveOrphanedSessionsAsync - skipping garbage collection");
                return 0;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // Prima: ottieni lista di sessioni orfane per logging
                var orphanedSessionsSql = $@"
                    SELECT session_id, name, working_directory
                    FROM Sessions
                    WHERE session_id NOT IN ({string.Join(",", validSessionIds.Select(id => $"'{id}'"))})";

                var orphanedSessions = new List<(string sessionId, string name, string workingDir)>();

                using (var selectCommand = new MySqlCommand(orphanedSessionsSql, connection))
                {
                    using var reader = await selectCommand.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        orphanedSessions.Add((
                            reader.GetString("session_id"),
                            reader.IsDBNull(reader.GetOrdinal("name")) ? "(no name)" : reader.GetString("name"),
                            reader.GetString("working_directory")
                        ));
                    }
                }

                if (orphanedSessions.Count == 0)
                {
                    Log.Information("No orphaned sessions found - database is clean");
                    return 0;
                }

                // Log dettagliato delle sessioni da rimuovere
                Log.Warning("Found {Count} orphaned sessions (files deleted from filesystem):", orphanedSessions.Count);
                foreach (var session in orphanedSessions)
                {
                    Log.Warning("  - Removing: {SessionId} | Name: {Name} | WorkingDir: {WorkingDir}",
                        session.sessionId, session.name, session.workingDir);
                }

                // DELETE: rimuovi sessioni orfane
                // NOTA: I messaggi associati verranno rimossi automaticamente tramite CASCADE DELETE
                // se la foreign key è configurata con ON DELETE CASCADE
                var deleteSql = $@"
                    DELETE FROM Sessions
                    WHERE session_id NOT IN ({string.Join(",", validSessionIds.Select(id => $"'{id}'"))})";

                using var deleteCommand = new MySqlCommand(deleteSql, connection);
                var rowsDeleted = await deleteCommand.ExecuteNonQueryAsync();

                Log.Information("Garbage collection completed: removed {Count} orphaned sessions", rowsDeleted);
                return rowsDeleted;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to remove orphaned sessions");
                throw;
            }
        }

        /// <summary>
        /// Inserisce una nuova sessione nel DB se non esiste già.
        /// Utilizza INSERT IGNORE per evitare di sovrascrivere sessioni esistenti.
        /// </summary>
        /// <param name="sessionId">UUID della sessione</param>
        /// <param name="name">Nome della sessione (può essere vuoto)</param>
        /// <param name="workingDirectory">Working directory della sessione</param>
        /// <param name="lastActivity">Data ultima attività (default NOW se non specificata)</param>
        /// <returns>True se la sessione è stata inserita, False se esisteva già</returns>
        public async Task<bool> InsertSessionAsync(string sessionId, string? name, string workingDirectory, DateTime? lastActivity = null)
        {
            const string sql = @"
                INSERT IGNORE INTO Sessions (session_id, name, working_directory, status, last_activity)
                VALUES (@SessionId, @Name, @WorkingDirectory, 'closed', @LastActivity)";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@SessionId", sessionId);
                command.Parameters.AddWithValue("@Name", string.IsNullOrWhiteSpace(name) ? (object)DBNull.Value : name);
                command.Parameters.AddWithValue("@WorkingDirectory", workingDirectory);
                command.Parameters.AddWithValue("@LastActivity", lastActivity ?? DateTime.Now);

                var rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    Log.Debug("Inserted new session: {SessionId} - Name: {Name}, WorkingDir: {WorkingDir}, LastActivity: {LastActivity}",
                        sessionId, name ?? "(empty)", workingDirectory, lastActivity ?? DateTime.Now);
                    return true;
                }
                else
                {
                    Log.Debug("Session already exists: {SessionId}", sessionId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to insert session: {SessionId}", sessionId);
                throw;
            }
        }

        /// <summary>
        /// Aggiorna il nome di una sessione
        /// </summary>
        public async Task UpdateSessionNameAsync(string sessionId, string name)
        {
            const string sql = "UPDATE Sessions SET name = @Name WHERE session_id = @SessionId";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Name", name);
                command.Parameters.AddWithValue("@SessionId", sessionId);

                await command.ExecuteNonQueryAsync();

                Log.Information("Updated session name: {SessionId} → {Name}", sessionId, name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update session name: {SessionId}", sessionId);
                throw;
            }
        }

        /// <summary>
        /// Aggiorna lo status di una sessione
        /// </summary>
        public async Task UpdateSessionStatusAsync(string sessionId, string status)
        {
            const string sql = "UPDATE Sessions SET status = @Status WHERE session_id = @SessionId";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Status", status);
                command.Parameters.AddWithValue("@SessionId", sessionId);

                await command.ExecuteNonQueryAsync();

                Log.Information("Updated session status: {SessionId} → {Status}", sessionId, status);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update session status: {SessionId}", sessionId);
                throw;
            }
        }

        // ===== METODI PER GESTIONE MESSAGGI COMPLETI CON METADATA =====

        /// <summary>
        /// Restituisce l'insieme dei campi JSON noti dal formato .jsonl di Claude Code
        /// </summary>
        public HashSet<string> GetKnownJsonFields()
        {
            return new HashSet<string>
            {
                // Campi root comuni
                "type", "message", "timestamp", "uuid", "sessionId",
                "parentUuid", "isSidechain", "userType", "cwd",
                "version", "gitBranch", "requestId",

                // Campi message object
                "role", "content", "model", "id", "stop_reason",
                "stop_sequence", "usage",

                // Campi content array items
                "text", "type",

                // Campi tool_use
                "name", "input", "tool_use_id",

                // Campi tool_result
                "is_error",

                // Campi usage
                "input_tokens", "output_tokens",
                "cache_creation_input_tokens", "cache_read_input_tokens",

                // Altri campi osservati
                "summary", "metadata"
            };
        }

        /// <summary>
        /// Rileva campi sconosciuti in un elemento JSON confrontandolo con i campi noti
        /// </summary>
        public List<string> DetectUnknownFields(System.Text.Json.JsonElement root, HashSet<string> knownFields)
        {
            var unknownFields = new List<string>();
            DetectUnknownFieldsRecursive(root, knownFields, "", unknownFields);
            return unknownFields;
        }

        private void DetectUnknownFieldsRecursive(
            System.Text.Json.JsonElement element,
            HashSet<string> knownFields,
            string path,
            List<string> unknownFields)
        {
            foreach (var property in element.EnumerateObject())
            {
                var fieldName = string.IsNullOrEmpty(path)
                    ? property.Name
                    : $"{path}.{property.Name}";

                // Controlla solo i nomi delle proprietà (non il path completo)
                if (!knownFields.Contains(property.Name))
                {
                    unknownFields.Add(fieldName);
                }

                // Ricorsione per oggetti nested
                if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    DetectUnknownFieldsRecursive(property.Value, knownFields, fieldName, unknownFields);
                }
                // Ricorsione per array di oggetti
                else if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    int index = 0;
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            DetectUnknownFieldsRecursive(item, knownFields, $"{fieldName}[{index}]", unknownFields);
                        }
                        index++;
                    }
                }
            }
        }

        /// <summary>
        /// Legge le ultime N righe non vuote da un file .jsonl
        /// Ottimizzato: legge solo ultimi 32KB invece di tutto il file
        /// </summary>
        public async Task<List<string>> ReadLastLinesFromFileAsync(
            string filePath,
            int maxLines = 10,
            int bufferSizeKb = 32)
        {
            if (!System.IO.File.Exists(filePath))
                return new List<string>();

            try
            {
                await using var fs = new System.IO.FileStream(
                    filePath,
                    System.IO.FileMode.Open,
                    System.IO.FileAccess.Read,
                    System.IO.FileShare.ReadWrite  // Permette scritture concorrenti
                );

                var bufferSize = bufferSizeKb * 1024;
                var offset = Math.Max(0, fs.Length - bufferSize);
                fs.Seek(offset, System.IO.SeekOrigin.Begin);

                using var reader = new System.IO.StreamReader(fs, System.Text.Encoding.UTF8);
                var lines = new List<string>();

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        lines.Add(line);
                }

                // Ritorna ultime N righe
                return lines.Skip(Math.Max(0, lines.Count - maxLines)).ToList();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to read last lines from {FilePath}", filePath);
                return new List<string>();
            }
        }

        /// <summary>
        /// Importa messaggi da file .jsonl nel database
        /// Rileva campi sconosciuti e interrompe se ne trova
        /// </summary>
        /// <returns>Tupla: (messaggi importati, campi sconosciuti, uuid con errore)</returns>
        public async Task<(int imported, List<string> unknownFields, string? errorUuid)>
            ImportMessagesFromJsonlAsync(string sessionId, string jsonlFilePath)
        {
            var knownFields = GetKnownJsonFields();
            int imported = 0;

            try
            {
                using var reader = new System.IO.StreamReader(jsonlFilePath);
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var json = System.Text.Json.JsonDocument.Parse(line);
                    var root = json.RootElement;

                    // RILEVA CAMPI SCONOSCIUTI
                    var unknownFields = DetectUnknownFields(root, knownFields);
                    if (unknownFields.Count > 0)
                    {
                        var errorUuid = root.TryGetProperty("uuid", out var u) ? u.GetString() : null;
                        return (imported, unknownFields, errorUuid);
                    }

                    // NESSUN FILTRO - salva TUTTI i tipi
                    var type = root.GetProperty("type").GetString();

                    // Estrai metadati base
                    var uuid = root.TryGetProperty("uuid", out var uuidProp) ? uuidProp.GetString() : null;
                    var timestamp = root.TryGetProperty("timestamp", out var tProp)
                        ? DateTime.Parse(tProp.GetString())
                        : DateTime.UtcNow;

                    // Verifica se già presente (via uuid)
                    if (!string.IsNullOrEmpty(uuid) && await MessageExistsByUuidAsync(uuid))
                    {
                        Log.Debug("Message with UUID {Uuid} already exists, skipping", uuid);
                        continue;
                    }

                    // Estrai contenuto base per salvare
                    string content = ExtractBasicContent(root);

                    // Salva nel DB (metodo semplificato per import batch)
                    await SaveMessageAsync(sessionId, type ?? "unknown", content, timestamp, uuid);

                    imported++;
                }

                return (imported, new List<string>(), null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to import messages from {FilePath}", jsonlFilePath);
                throw;
            }
        }

        private async Task<bool> MessageExistsByUuidAsync(string uuid)
        {
            const string sql = "SELECT COUNT(*) FROM messages WHERE uuid = @Uuid";

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Uuid", uuid);

            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            return count > 0;
        }

        private string ExtractBasicContent(System.Text.Json.JsonElement root)
        {
            try
            {
                if (root.TryGetProperty("message", out var messageObj))
                {
                    if (messageObj.TryGetProperty("content", out var contentProp))
                    {
                        if (contentProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            return contentProp.GetString() ?? "";
                        }
                        else if (contentProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var sb = new System.Text.StringBuilder();
                            foreach (var item in contentProp.EnumerateArray())
                            {
                                if (item.TryGetProperty("text", out var textProp))
                                {
                                    sb.AppendLine(textProp.GetString());
                                }
                            }
                            return sb.ToString();
                        }
                    }
                }

                // Fallback: salva JSON completo
                return root.GetRawText();
            }
            catch
            {
                return root.GetRawText();
            }
        }
    }
}
