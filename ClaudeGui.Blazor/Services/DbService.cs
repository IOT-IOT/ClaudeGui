using ClaudeGui.Blazor.Data;
using ClaudeGui.Blazor.Models;
using ClaudeGui.Blazor.Models.Entities;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClaudeGui.Blazor.Services
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
        private readonly IDbContextFactory<ClaudeGuiDbContext>? _dbContextFactory;

        /// <summary>
        /// Constructor. Builds connection string from hardcoded values + User Secrets credentials.
        /// </summary>
        /// <param name="username">Database username from User Secrets</param>
        /// <param name="password">Database password from User Secrets</param>
        /// <param name="dbContextFactory">Entity Framework DbContextFactory (optional, per migrazione graduale)</param>
        public DbService(string username, string password, IDbContextFactory<ClaudeGuiDbContext>? dbContextFactory = null)
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

            _dbContextFactory = dbContextFactory;

            Log.Information("DbService initialized with connection: {Host}:{Port}/{Database} (EF Core: {EFEnabled})",
                            DB_HOST, DB_PORT, DB_NAME, _dbContextFactory != null);
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

        // METODO LEGACY RIMOSSO - Usava ConversationSession e tabella conversations
        // Sostituito da: InsertSessionAsync(string sessionId, string? name, string workingDirectory, DateTime? lastActivity)

        /// <summary>
        /// Updates the status of a conversation session.
        /// Used for marking as 'killed' or 'closed'.
        /// </summary>
        public async Task UpdateStatusAsync(string sessionId, string status)
        {
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                var rowsAffected = await dbContext.Conversations
                    .Where(c => c.SessionId == sessionId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.Status, status)
                        .SetProperty(c => c.UpdatedAt, DateTime.Now));

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
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                await dbContext.Conversations
                    .Where(c => c.SessionId == sessionId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.LastActivity, DateTime.Now)
                        .SetProperty(c => c.UpdatedAt, DateTime.Now));

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
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                var rowsAffected = await dbContext.Conversations
                    .Where(c => c.SessionId == sessionId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.WorkingDirectory, workingDirectory)
                        .SetProperty(c => c.UpdatedAt, DateTime.Now));

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

        // METODO LEGACY RIMOSSO - Usava ConversationSession e tabella conversations
        // Sostituito da: GetOpenSessionsAsync() che usa la tabella Sessions e l'entity Session

        /// <summary>
        /// Batch updates multiple sessions to 'closed' status.
        /// Used when closing the application normally.
        /// </summary>
        public async Task UpdateStatusBatchAsync(List<string> sessionIds, string status)
        {
            if (sessionIds == null || sessionIds.Count == 0)
                return;

            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                var rowsAffected = await dbContext.Conversations
                    .Where(c => sessionIds.Contains(c.SessionId))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(c => c.Status, status)
                        .SetProperty(c => c.UpdatedAt, DateTime.Now));

                Log.Information("Batch updated {Count} sessions to status: {Status}", rowsAffected, status);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to batch update session status");
            }
        }

        /// <summary>
        /// Salva un singolo messaggio nel database in modalità standalone (con SaveChanges automatico).
        /// Usare questo metodo per salvare messaggi singoli in tempo reale.
        /// Per batch import, usare SaveMessageAsync con DbContext condiviso.
        /// </summary>
        /// <param name="conversationId">ID della sessione di conversazione</param>
        /// <param name="role">Ruolo del mittente</param>
        /// <param name="content">Contenuto del messaggio</param>
        /// <param name="timestamp">Timestamp del messaggio</param>
        /// <param name="uuid">UUID univoco del messaggio</param>
        /// <param name="parentUuid">UUID del messaggio parent</param>
        /// <param name="version">Versione Claude</param>
        /// <param name="gitBranch">Branch Git</param>
        /// <param name="isSidechain">Flag sidechain</param>
        /// <param name="cwd">Current working directory</param>
        /// <param name="model">Modello Claude usato</param>
        /// <param name="usageJson">JSON con usage info</param>
        /// <param name="messageType">Tipo di messaggio</param>
        public async Task SaveMessageStandaloneAsync(
            string conversationId,
            string role,
            string content,
            DateTime? timestamp = null,
            string? uuid = null,
            string? version = null,
            string? cwd = null,
            string? model = null,
            string? usageJson = null,
            string? messageType = null)
        {
            try
            {
                // Crea DbContext dedicato per questa operazione
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                // Carica UUIDs esistenti per questa sessione (ottimizzazione per evitare duplicati)
                var existingUuids = await dbContext.Messages
                    .Where(m => m.ConversationId == conversationId)
                    .Select(m => m.Uuid)
                    .ToHashSetAsync();

                // Salva il messaggio usando il metodo batch (senza SaveChanges)
                await SaveMessageAsync(
                    existingUuids,
                    dbContext,
                    conversationId,
                    role,
                    content,
                    timestamp,
                    uuid,
                    version,
                    cwd,
                    model,
                    usageJson,
                    messageType);

                // Salva nel database
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save standalone message for session: {SessionId}, uuid: {Uuid}",
                    conversationId, uuid);
                // Non fare throw - il salvataggio dei messaggi è opzionale e non deve bloccare l'app
            }
        }

        /// <summary>
        /// LEGACY OVERLOAD: Wrapper per future implementation (messages_from_jsonl).
        /// Accetta parametri obsoleti ma li ignora, delegando alla nuova signature.
        /// </summary>
        [Obsolete("Use SaveMessageStandaloneAsync with new signature (without obsolete parameters)")]
        public async Task SaveMessageStandaloneAsync(
            string conversationId,
            string role,
            string content,
            DateTime? timestamp,
            string? uuid,
            string? parentUuid,        // IGNORED
            string? version,
            string? gitBranch,         // IGNORED
            bool? isSidechain,         // IGNORED
            string? userType,          // IGNORED
            string? cwd,
            string? requestId,         // IGNORED
            string? model,
            string? usageJson,
            string? messageType)
        {
            // Delega alla firma pulita ignorando parametri obsoleti
            await SaveMessageStandaloneAsync(
                conversationId,
                role,
                content,
                timestamp,
                uuid,
                version,
                cwd,
                model,
                usageJson,
                messageType);
        }

        /// <summary>
        /// Salva un messaggio nella tabella messages (versione semplificata per retrocompatibilità).
        /// </summary>
        /// <param name="conversationId">ID della sessione di conversazione</param>
        /// <param name="role">Ruolo del mittente: "user" o "assistant"</param>
        /// <param name="content">Contenuto del messaggio (può includere markdown)</param>
        public async Task SaveMessageAsync(string conversationId, string role, string content)
        {
            await SaveMessageStandaloneAsync(conversationId, role, content);
        }

        /// <summary>
        /// Salva un messaggio nel database con metadati completi dal file .jsonl (modalità batch).
        /// Il numero di sequenza viene calcolato automaticamente come MAX(sequence) + 1 per la conversazione.
        /// NOTA: Questo metodo NON esegue SaveChanges - usato per batch import.
        /// Per salvare singoli messaggi, usare SaveMessageStandaloneAsync.
        /// </summary>
        /// <param name="existingUuids">HashSet con UUID già esistenti (per evitare duplicati senza query database)</param>
        /// <param name="dbContext">DbContext da usare (modalità batch, il chiamante gestisce SaveChanges)</param>
        /// <param name="conversationId">ID della sessione di conversazione</param>
        /// <param name="role">Ruolo del mittente</param>
        /// <param name="content">Contenuto del messaggio</param>
        /// <param name="timestamp">Timestamp del messaggio</param>
        /// <param name="uuid">UUID univoco del messaggio</param>
        /// <param name="parentUuid">UUID del messaggio parent</param>
        /// <param name="version">Versione Claude</param>
        /// <param name="gitBranch">Branch Git</param>
        /// <param name="isSidechain">Flag sidechain</param>
        /// <param name="userType">Tipo di utente</param>
        /// <param name="cwd">Current working directory</param>
        /// <param name="requestId">Request ID</param>
        /// <param name="model">Modello Claude usato</param>
        /// <param name="usageJson">JSON con usage info</param>
        /// <param name="messageType">Tipo di messaggio</param>
        public async Task SaveMessageAsync(
            HashSet<string> existingUuids,
            ClaudeGuiDbContext dbContext,
            string conversationId = "",
            string role = "",
            string content = "",
            DateTime? timestamp = null,
            string? uuid = null,
            string? version = null,
            string? cwd = null,
            string? model = null,
            string? usageJson = null,
            string? messageType = null)
        {
            // Verifica duplicati usando HashSet (O(1) lookup, no database round-trip)
            if (!string.IsNullOrEmpty(uuid) && existingUuids.Contains(uuid))
            {
                // Messaggio già esistente, skip
                return;
            }


            // Crea il nuovo messaggio
            var message = new Message
            {
                ConversationId = conversationId,
                MessageType = messageType ?? role,
                Content = content,
                Timestamp = timestamp ?? DateTime.UtcNow,
                Uuid = uuid,
                Version = version,
                Cwd = cwd,
                Model = model,
                UsageJson = usageJson
            };

            // Aggiungi al ChangeTracker (il chiamante farà SaveChanges in batch)
            dbContext.Messages.Add(message);

            // Aggiungi UUID all'HashSet per evitare duplicati nei prossimi messaggi dello stesso batch
            if (!string.IsNullOrEmpty(uuid))
            {
                existingUuids.Add(uuid);
            }
        }

        /// <summary>
        /// LEGACY OVERLOAD: Wrapper per future implementation (messages_from_jsonl).
        /// Accetta parametri obsoleti ma li ignora, delegando alla nuova signature.
        /// </summary>
        [Obsolete("Use SaveMessageAsync with new signature (without obsolete parameters)")]
        public async Task SaveMessageAsync(
            HashSet<string> existingUuids,
            ClaudeGuiDbContext dbContext,
            string conversationId,
            string role,
            string content,
            DateTime? timestamp,
            string? uuid,
            string? parentUuid,        // IGNORED
            string? version,
            string? gitBranch,         // IGNORED
            bool? isSidechain,         // IGNORED
            string? userType,          // IGNORED
            string? cwd,
            string? requestId,         // IGNORED
            string? model,
            string? usageJson,
            string? messageType)
        {
            // Delega alla firma pulita ignorando parametri obsoleti
            await SaveMessageAsync(
                existingUuids,
                dbContext,
                conversationId,
                role,
                content,
                timestamp,
                uuid,
                version,
                cwd,
                model,
                usageJson,
                messageType);
        }

        /// <summary>
        /// Salva un summary nel database in modalità batch (senza SaveChanges).
        /// Usato durante l'import batch di messaggi .jsonl quando viene rilevato un messaggio type="summary".
        /// Il chiamante deve gestire SaveChanges.
        /// </summary>
        /// <param name="dbContext">DbContext da usare (modalità batch)</param>
        /// <param name="sessionId">ID della sessione di conversazione</param>
        /// <param name="timestamp">Timestamp del summary</param>
        /// <param name="summaryText">Testo del summary generato da Claude</param>
        /// <param name="leafUuid">UUID del messaggio finale (opzionale)</param>
        public void SaveSummary(
            ClaudeGuiDbContext dbContext,
            string sessionId,
            DateTime timestamp,
            string summaryText,
            string? leafUuid = null)
        {
            // Crea l'entity Summary
            var summary = new Summary
            {
                SessionId = sessionId,
                Timestamp = timestamp,
                SummaryText = summaryText,
                LeafUuid = leafUuid
            };

            // Aggiungi al ChangeTracker (il chiamante farà SaveChanges in batch)
            dbContext.Summaries.Add(summary);
        }

        /// <summary>
        /// Salva un singolo summary nel database in modalità standalone (con SaveChanges automatico).
        /// Usato quando arriva un messaggio type="summary" in tempo reale (non durante batch import).
        /// </summary>
        /// <param name="sessionId">ID della sessione di conversazione</param>
        /// <param name="timestamp">Timestamp del summary</param>
        /// <param name="summaryText">Testo del summary generato da Claude</param>
        /// <param name="leafUuid">UUID del messaggio finale (opzionale)</param>
        public async Task SaveSummaryStandaloneAsync(
            string sessionId,
            DateTime timestamp,
            string summaryText,
            string? leafUuid = null)
        {
            try
            {
                // Crea DbContext dedicato per questa operazione
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                // Salva il summary usando il metodo batch (senza SaveChanges)
                SaveSummary(dbContext, sessionId, timestamp, summaryText, leafUuid);

                // Salva nel database
                await dbContext.SaveChangesAsync();

                Log.Information("Saved summary for session {SessionId}: {Summary}",
                    sessionId, summaryText);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save standalone summary for session: {SessionId}",
                    sessionId);
                // Non fare throw - il salvataggio dei summary è opzionale e non deve bloccare l'app
            }
        }

        /// <summary>
        /// Salva un file history snapshot nel database in modalità batch (senza SaveChanges).
        /// Usato durante l'import batch di messaggi .jsonl quando viene rilevato un messaggio type="file-history-snapshot".
        /// Il chiamante deve gestire SaveChanges.
        /// </summary>
        /// <param name="dbContext">DbContext da usare (modalità batch)</param>
        /// <param name="sessionId">ID della sessione di conversazione</param>
        /// <param name="timestamp">Timestamp dello snapshot</param>
        /// <param name="messageId">Message ID univoco dello snapshot</param>
        /// <param name="trackedFileBackupsJson">JSON con i backup dei file tracciati</param>
        /// <param name="isSnapshotUpdate">Flag aggiornamento snapshot</param>
        public void SaveFileHistorySnapshot(
            ClaudeGuiDbContext dbContext,
            string sessionId,
            DateTime timestamp,
            string messageId,
            string trackedFileBackupsJson,
            bool isSnapshotUpdate)
        {
            // Crea l'entity FileHistorySnapshot
            var snapshot = new FileHistorySnapshot
            {
                SessionId = sessionId,
                Timestamp = timestamp,
                MessageId = messageId,
                TrackedFileBackupsJson = trackedFileBackupsJson,
                IsSnapshotUpdate = isSnapshotUpdate
            };

            // Aggiungi al ChangeTracker (il chiamante farà SaveChanges in batch)
            dbContext.FileHistorySnapshots.Add(snapshot);
        }

        /// <summary>
        /// Salva un singolo file history snapshot nel database in modalità standalone (con SaveChanges automatico).
        /// Usato quando arriva un messaggio type="file-history-snapshot" in tempo reale (non durante batch import).
        /// </summary>
        /// <param name="sessionId">ID della sessione di conversazione</param>
        /// <param name="timestamp">Timestamp dello snapshot</param>
        /// <param name="messageId">Message ID univoco dello snapshot</param>
        /// <param name="trackedFileBackupsJson">JSON con i backup dei file tracciati</param>
        /// <param name="isSnapshotUpdate">Flag aggiornamento snapshot</param>
        public async Task SaveFileHistorySnapshotStandaloneAsync(
            string sessionId,
            DateTime timestamp,
            string messageId,
            string trackedFileBackupsJson,
            bool isSnapshotUpdate)
        {
            try
            {
                // Crea DbContext dedicato per questa operazione
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                // Salva lo snapshot usando il metodo batch (senza SaveChanges)
                SaveFileHistorySnapshot(dbContext, sessionId, timestamp, messageId,
                    trackedFileBackupsJson, isSnapshotUpdate);

                // Salva nel database
                await dbContext.SaveChangesAsync();

                Log.Information("Saved file history snapshot for session {SessionId}: messageId={MessageId}",
                    sessionId, messageId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save standalone file history snapshot for session: {SessionId}",
                    sessionId);
                // Non fare throw - il salvataggio degli snapshot è opzionale e non deve bloccare l'app
            }
        }

        /// <summary>
        /// Salva una queue operation nel database in modalità batch (senza SaveChanges).
        /// Usato durante l'import batch di messaggi .jsonl quando viene rilevato un messaggio type="queue-operation".
        /// Il chiamante deve gestire SaveChanges.
        /// </summary>
        /// <param name="dbContext">DbContext da usare (modalità batch)</param>
        /// <param name="sessionId">ID della sessione di conversazione</param>
        /// <param name="timestamp">Timestamp dell'operazione</param>
        /// <param name="operation">Tipo di operazione (es. "enqueue")</param>
        /// <param name="content">Contenuto del messaggio accodato</param>
        public void SaveQueueOperation(
            ClaudeGuiDbContext dbContext,
            string sessionId,
            DateTime timestamp,
            string operation,
            string content)
        {
            // Crea l'entity QueueOperation
            var queueOp = new QueueOperation
            {
                SessionId = sessionId,
                Timestamp = timestamp,
                Operation = operation,
                Content = content
            };

            // Aggiungi al ChangeTracker (il chiamante farà SaveChanges in batch)
            dbContext.QueueOperations.Add(queueOp);
        }

        /// <summary>
        /// Salva una singola queue operation nel database in modalità standalone (con SaveChanges automatico).
        /// Usato quando arriva un messaggio type="queue-operation" in tempo reale (non durante batch import).
        /// </summary>
        /// <param name="sessionId">ID della sessione di conversazione</param>
        /// <param name="timestamp">Timestamp dell'operazione</param>
        /// <param name="operation">Tipo di operazione (es. "enqueue")</param>
        /// <param name="content">Contenuto del messaggio accodato</param>
        public async Task SaveQueueOperationStandaloneAsync(
            string sessionId,
            DateTime timestamp,
            string operation,
            string content)
        {
            try
            {
                // Crea DbContext dedicato per questa operazione
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                // Salva la queue operation usando il metodo batch (senza SaveChanges)
                SaveQueueOperation(dbContext, sessionId, timestamp, operation, content);

                // Salva nel database
                await dbContext.SaveChangesAsync();

                Log.Information("Saved queue operation for session {SessionId}: operation={Operation}",
                    sessionId, operation);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save standalone queue operation for session: {SessionId}",
                    sessionId);
                // Non fare throw - il salvataggio delle queue operations è opzionale e non deve bloccare l'app
            }
        }

        /// <summary>
        /// Recupera gli ultimi N messaggi di una conversazione in ordine cronologico usando Entity Framework Core.
        /// Usato per visualizzare la storia quando si riprende una sessione.
        /// </summary>
        /// <param name="conversationId">ID della sessione di conversazione</param>
        /// <param name="count">Numero massimo di messaggi da recuperare (default 10)</param>
        /// <returns>Lista di entity Message ordinati cronologicamente (dal più vecchio al più recente)</returns>
        public async Task<List<Message>> GetLastMessagesAsync(string conversationId, int count = 10)
        {
            if (count <= 0)
            {
                Log.Debug("GetLastMessagesAsync called with count={Count}, returning empty list", count);
                return new List<Message>();
            }

            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                // Prendi gli ultimi N messaggi ordinati per Id decrescente (più recenti),
                // poi invertili per ordine cronologico crescente (dal più vecchio al più recente)
                var messages = await dbContext.Messages
                    .AsNoTracking()
                    .Where(m => m.ConversationId == conversationId)
                    .OrderByDescending(m => m.Id)
                    .Take(count)
                    .ToListAsync();

                // Inverti l'ordine per visualizzazione cronologica (dal più vecchio al più recente)
                messages.Reverse();

                Log.Information("Retrieved {Count} historical messages for session: {SessionId}", messages.Count, conversationId);
                return messages;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to retrieve messages for session: {SessionId}", conversationId);
                return new List<Message>(); // Ritorna lista vuota in caso di errore
            }
        }

        // ===== METODI PER TABELLA SESSIONS (Multi-Sessione con Tab) =====

        /// <summary>
        /// Ottiene una sessione dal DB per session_id usando Entity Framework Core
        /// </summary>
        /// <param name="sessionId">UUID della sessione da cercare</param>
        /// <returns>Entity Session o null se non trovata</returns>
        public async Task<Session?> GetSessionByIdAsync(string sessionId)
        {
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                var session = await dbContext.Sessions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.SessionId == sessionId);

                return session;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get session by ID: {SessionId}", sessionId);
                return null;
            }
        }

        /// <summary>
        /// Ottiene TUTTE le sessioni dal database usando Entity Framework Core.
        /// Usato per cache in-memory durante la sincronizzazione filesystem.
        /// </summary>
        /// <returns>Lista di tutte le entity Session</returns>
        public async Task<List<Session>> GetAllSessionsAsync()
        {
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                var sessions = await dbContext.Sessions
                    .AsNoTracking()
                    .ToListAsync();

                Log.Debug("Retrieved {Count} total sessions from database for caching", sessions.Count);
                return sessions;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get all sessions");
                return new List<Session>();
            }
        }

        /// <summary>
        /// Ottiene tutte le sessioni aperte (status = 'open') usando Entity Framework Core
        /// </summary>
        /// <returns>Lista di entity Session con status 'open', ordinate per ultima attività decrescente</returns>
        public async Task<List<Session>> GetOpenSessionsAsync()
        {
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                var sessions = await dbContext.Sessions
                    .AsNoTracking()
                    .Where(s => s.Status == "open")
                    .OrderByDescending(s => s.LastActivity)
                    .ToListAsync();

                Log.Information("Retrieved {Count} open sessions", sessions.Count);
                return sessions;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get open sessions");
                return new List<Session>();
            }
        }

        /// <summary>
        /// Ottiene tutte le sessioni chiuse (status = 'closed') non escluse usando Entity Framework Core.
        /// Le sessioni escluse (excluded = TRUE) non vengono restituite.
        /// </summary>
        /// <returns>Lista di entity Session chiuse e non escluse, ordinate per ultima attività decrescente</returns>
        public async Task<List<Session>> GetClosedSessionsAsync()
        {
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                var sessions = await dbContext.Sessions
                    .AsNoTracking()
                    .Where(s => s.Status == "closed" && s.Excluded == false)
                    .OrderByDescending(s => s.LastActivity)
                    .ToListAsync();

                Log.Information("Retrieved {Count} closed non-excluded sessions", sessions.Count);
                return sessions;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get closed sessions");
                return new List<Session>();
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
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                var session = await dbContext.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId);
                if (session != null)
                {
                    session.Processed = true;
                    session.Excluded = isExcluded;
                    session.ExcludedReason = excludedReason;
                    await dbContext.SaveChangesAsync();

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
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                var rowsAffected = await dbContext.Sessions
                    .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Processed, false));

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
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                // Prima: ottieni lista di sessioni orfane per logging
                var orphanedSessions = await dbContext.Sessions
                    .Where(s => !validSessionIds.Contains(s.SessionId))
                    .Select(s => new { s.SessionId, s.Name, s.WorkingDirectory })
                    .ToListAsync();

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
                        session.SessionId, session.Name ?? "(no name)", session.WorkingDirectory);
                }

                // DELETE: rimuovi sessioni orfane
                // NOTA: I messaggi associati verranno rimossi automaticamente tramite CASCADE DELETE
                // se la foreign key è configurata con ON DELETE CASCADE
                var rowsDeleted = await dbContext.Sessions
                    .Where(s => !validSessionIds.Contains(s.SessionId))
                    .ExecuteDeleteAsync();

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
        /// Verifica se esiste già una sessione con il nome specificato.
        /// </summary>
        /// <param name="name">Nome della sessione da cercare</param>
        /// <returns>True se esiste già una sessione con quel nome, False altrimenti</returns>
        public async Task<bool> SessionNameExistsAsync(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return false; // Nome vuoto non può essere duplicato
                }

                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                // Verifica se esiste già una sessione con questo nome
                var exists = await dbContext.Sessions.AnyAsync(s => s.Name == name);

                Log.Debug("SessionNameExists check for '{Name}': {Exists}", name, exists);
                return exists;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to check if session name exists: {Name}", name);
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
        /// <param name="status">Stato sessione: 'open' o 'closed' (default 'closed' per retrocompatibilità)</param>
        /// <returns>True se la sessione è stata inserita, False se esisteva già</returns>
        public async Task<bool> InsertSessionAsync(string sessionId, string? name, string workingDirectory, DateTime? lastActivity = null, string status = "closed")
        {
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                // Verifica se la sessione esiste già
                var exists = await dbContext.Sessions.AnyAsync(s => s.SessionId == sessionId);
                if (exists)
                {
                    Log.Debug("Session already exists: {SessionId}", sessionId);
                    return false;
                }

                // Crea nuova sessione
                var session = new Session
                {
                    SessionId = sessionId,
                    Name = string.IsNullOrWhiteSpace(name) ? null : name,
                    WorkingDirectory = workingDirectory,
                    Status = status,
                    LastActivity = lastActivity ?? DateTime.Now
                };

                dbContext.Sessions.Add(session);
                await dbContext.SaveChangesAsync();

                Log.Debug("Inserted new session: {SessionId} - Name: {Name}, WorkingDir: {WorkingDir}, LastActivity: {LastActivity}",
                    sessionId, name ?? "(empty)", workingDirectory, lastActivity ?? DateTime.Now);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to insert session: {SessionId}", sessionId);
                throw;
            }
        }

        /// <summary>
        /// Inserisce una nuova sessione o aggiorna lastActivity se esiste già.
        /// Usato quando si estrae SessionId da messaggi stdout.
        /// </summary>
        /// <param name="sessionId">UUID della sessione</param>
        /// <param name="name">Nome della sessione (opzionale)</param>
        /// <param name="workingDirectory">Working directory</param>
        /// <param name="lastActivity">Timestamp ultima attività</param>
        /// <returns>True se nuova sessione inserita, False se già esistente</returns>
        public async Task<bool> InsertOrUpdateSessionAsync(
            string sessionId,
            string? name,
            string workingDirectory,
            DateTime lastActivity)
        {
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                var existingSession = await dbContext.Sessions
                    .FirstOrDefaultAsync(s => s.SessionId == sessionId);

                if (existingSession != null)
                {
                    // Aggiorna lastActivity se esiste
                    existingSession.LastActivity = lastActivity;

                    // Aggiorna nome se vuoto e viene fornito
                    if (string.IsNullOrWhiteSpace(existingSession.Name) && !string.IsNullOrWhiteSpace(name))
                    {
                        existingSession.Name = name;
                    }

                    await dbContext.SaveChangesAsync();
                    Log.Debug("Updated lastActivity for session: {SessionId}", sessionId);
                    return false; // Esisteva già
                }
                else
                {
                    // Inserisci nuova sessione
                    var session = new Session
                    {
                        SessionId = sessionId,
                        Name = string.IsNullOrWhiteSpace(name) ? null : name,
                        WorkingDirectory = workingDirectory,
                        Status = "open",
                        LastActivity = lastActivity,
                        CreatedAt = DateTime.Now,
                        Processed = true,  // Sessioni create via UI sono già valide, non necessitano analisi
                        Excluded = false
                    };
                    dbContext.Sessions.Add(session);
                    await dbContext.SaveChangesAsync();
                    Log.Information("Inserted new session: {SessionId}", sessionId);
                    return true; // Nuova sessione
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to insert or update session: {SessionId}", sessionId);
                throw;
            }
        }

        /// <summary>
        /// Aggiorna il nome di una sessione
        /// </summary>
        public async Task UpdateSessionNameAsync(string sessionId, string name)
        {
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                var session = await dbContext.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId);
                if (session != null)
                {
                    session.Name = name;
                    await dbContext.SaveChangesAsync();

                    Log.Information("Updated session name: {SessionId} → {Name}", sessionId, name);
                }
                else
                {
                    Log.Warning("Session not found for name update: {SessionId}", sessionId);
                }
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
            try
            {
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                var session = await dbContext.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId);
                if (session != null)
                {
                    session.Status = status;
                    await dbContext.SaveChangesAsync();

                    Log.Information("Updated session status: {SessionId} → {Status}", sessionId, status);
                }
                else
                {
                    Log.Warning("Session not found for status update: {SessionId}", sessionId);
                }
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
                "cache_creation", "service_tier",

                // Campi cache_creation object (nested in usage)
                "ephemeral_5m_input_tokens", "ephemeral_1h_input_tokens",

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
        /// FUTURE IMPLEMENTATION: Importa messaggi da file .jsonl nel database.
        /// Questo metodo importerà nella tabella 'messages_from_jsonl'.
        /// Attualmente usa la vecchia firma con parametri obsoleti (mantenuta per compatibilità futura).
        ///
        /// Rileva campi sconosciuti e mostra dialog per decidere se continuare o interrompere.
        /// </summary>
        /// <param name="sessionId">ID della sessione</param>
        /// <param name="jsonlFilePath">Path del file .jsonl</param>
        /// <param name="progressCallback">Callback per aggiornare progress (current, total)</param>
        /// <param name="unknownFieldsCallback">Callback per mostrare dialog unknown fields (jsonLine, uuid, unknownFields). Return true per continuare, false per interrompere.</param>
        /// <param name="cancellationToken">Token per annullare l'operazione</param>
        /// <returns>MessageImportResult con dettagli completi dell'import</returns>
        public async Task<Models.MessageImportResult> ImportMessagesFromJsonlAsync(
                string sessionId,
                string jsonlFilePath,
                IProgress<(int current, int total)>? progressCallback = null,
                Func<string, string, List<string>, Task<bool>>? unknownFieldsCallback = null,
                CancellationToken cancellationToken = default)
        {
            var knownFields = GetKnownJsonFields();
            var result = new Models.MessageImportResult();
            int totalLines = 0;

            try
            {
                // Prima passata: conta le righe totali per il progress
                if (progressCallback != null)
                {
                    using var countReader = new System.IO.StreamReader(jsonlFilePath);
                    while (!countReader.EndOfStream)
                    {
                        await countReader.ReadLineAsync();
                        totalLines++;
                    }
                    Log.Information("Total lines in file: {Total}", totalLines);
                }

                int lineNumber = 0;

                // Apre il file con accesso condiviso (read/write concorrente)
                using var fileStream = new System.IO.FileStream(
                    jsonlFilePath,
                    System.IO.FileMode.Open,
                    System.IO.FileAccess.Read,
                    System.IO.FileShare.ReadWrite  // Permette accesso concorrente in lettura e scrittura
                );
                using var reader = new System.IO.StreamReader(fileStream, System.Text.Encoding.UTF8);
                using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                // OTTIMIZZAZIONE: Carica TUTTI gli UUID esistenti per questa sessione una sola volta
                // Evita N query durante il loop (1 query iniziale invece di N query nel loop)
                // HashSet permette lookup O(1) invece di query database O(N)
                var existingUuids = await dbContext.Messages
                    .Where(m => m.ConversationId == sessionId)
                    .Select(m => m.Uuid)
                    .ToHashSetAsync(cancellationToken);

                Log.Information("Loaded {Count} existing UUIDs for session {SessionId} - will skip duplicates in memory",
                    existingUuids.Count, sessionId);

                while (!reader.EndOfStream)
                {
                    // Check cancellation
                    cancellationToken.ThrowIfCancellationRequested();

                    var line = await reader.ReadLineAsync();
                    lineNumber++;

                    progressCallback?.Report((lineNumber, totalLines));


                    if (string.IsNullOrWhiteSpace(line))
                    {
                        progressCallback?.Report((lineNumber, totalLines));
                        continue;
                    }

                    var json = System.Text.Json.JsonDocument.Parse(line);
                    var root = json.RootElement;

                    // RILEVA CAMPI SCONOSCIUTI - CHIEDE ALL'UTENTE COSA FARE
                    //var unknownFields = DetectUnknownFields(root, knownFields);
                    //List<String>? unknownFields = []; //Disabilitato permanentemente
                    //if (unknownFields.Count > 0 )  
                    //{
                    //    string contentunknown = ExtractBasicContent(root);
                    //    var messageUuid = root.TryGetProperty("uuid", out var u) ? u.GetString() : "unknown";
                    //    result.UnknownFieldsByMessage[messageUuid] = unknownFields;
                    //    result.SkippedCount++;

                    //    Log.Warning("Unknown fields found in message {Uuid}: {Fields}",
                    //        messageUuid, string.Join(", ", unknownFields));

                    //    // Chiama callback per mostrare dialog e decidere se continuare
                    //    if (unknownFieldsCallback != null)
                    //    {
                    //        bool shouldContinue = await unknownFieldsCallback(line, messageUuid ?? "unknown", unknownFields);
                    //        if (!shouldContinue)
                    //        {
                    //            Log.Information("Import interrupted by user at message {Uuid}", messageUuid);
                    //            throw new OperationCanceledException("Import interrotto dall'utente");
                    //        }
                    //    }

                    //    progressCallback?.Report((lineNumber, totalLines));
                    //    continue; // SKIP questo messaggio ma CONTINUA con i successivi
                    //}

                    // NESSUN FILTRO - salva TUTTI i tipi


                    

                    var type = root.GetProperty("type").GetString();
                    //continue;
                    // Estrai metadati base
                    var uuid = root.TryGetProperty("uuid", out var uuidProp) ? uuidProp.GetString() : null;
                    var timestamp = root.TryGetProperty("timestamp", out var tProp)
                        ? DateTime.Parse(tProp.GetString())
                        : DateTime.UtcNow;


                    //continue;
                    // Estrai TUTTI i metadata completi (sia snake_case che camelCase)
                    string? parentUuid = root.TryGetProperty("parent_uuid", out var pu1) ? pu1.GetString() :
                                        root.TryGetProperty("parentUuid", out var pu2) ? pu2.GetString() : null;
                    string? version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
                    string? gitBranch = root.TryGetProperty("git_branch", out var gb1) ? gb1.GetString() :
                                       root.TryGetProperty("gitBranch", out var gb2) ? gb2.GetString() : null;
                    bool? isSidechain = root.TryGetProperty("is_sidechain", out var isc1) ? isc1.GetBoolean() :
                                       root.TryGetProperty("isSidechain", out var isc2) ? (bool?)isc2.GetBoolean() : null;
                    string? userType = root.TryGetProperty("user_type", out var ut1) ? ut1.GetString() :
                                      root.TryGetProperty("userType", out var ut2) ? ut2.GetString() : null;
                    string? cwd = root.TryGetProperty("cwd", out var c) ? c.GetString() : null;

                    string? requestId = null;
                    string? model = null;
                    string? usageJson = null;
                    //continue;
                    // Per messaggi assistant, estrai requestId, model e usage
                    if (root.TryGetProperty("message", out var messageProp))
                    {
                        if (messageProp.TryGetProperty("id", out var idProp))
                            requestId = idProp.GetString();

                        if (messageProp.TryGetProperty("model", out var modelProp))
                            model = modelProp.GetString();

                        if (messageProp.TryGetProperty("usage", out var usageProp))
                        {
                            usageJson = usageProp.GetRawText();
                        }
                    }

                    // Estrai contenuto
                    string content = ExtractBasicContent(root);


                    // Check se è un messaggio di tipo "summary" - gestione speciale
                    if (type == "summary")
                    {
                        try
                        {
                            // Parse del content JSON per estrarre il campo "summary"
                            var contentJson = JsonDocument.Parse(content);
                            var summaryText = contentJson.RootElement.GetProperty("summary").GetString();
                            var leafUuid = contentJson.RootElement.TryGetProperty("leafUuid", out var leafUuidProp)
                                ? leafUuidProp.GetString()
                                : null;

                            // Salva nella tabella summaries (NON in messages)
                            SaveSummary(dbContext, sessionId, timestamp, summaryText ?? "", leafUuid);

                            result.ImportedCount++;
                            Log.Debug("Saved summary for session {SessionId}: {Summary}", sessionId, summaryText);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to parse summary content at line {LineNumber}: {Content}",
                                lineNumber, content);
                            result.SkippedCount++;
                        }

                        // Skip SaveMessageAsync per i summary
                        progressCallback?.Report((lineNumber, totalLines));
                        continue;
                    }

                    // Check se è un messaggio di tipo "file-history-snapshot" - gestione speciale
                    if (type == "file-history-snapshot")
                    {
                        try
                        {
                            // Parse del content JSON per estrarre i campi necessari
                            var contentJson = JsonDocument.Parse(content);
                            var messageId = contentJson.RootElement.GetProperty("messageId").GetString() ?? "";
                            var isSnapshotUpdate = contentJson.RootElement.TryGetProperty("isSnapshotUpdate", out var isuProp)
                                && isuProp.GetBoolean();

                            // Estrai snapshot.trackedFileBackups come JSON string
                            string trackedFileBackupsJson = "{}";
                            if (contentJson.RootElement.TryGetProperty("snapshot", out var snapshotProp))
                            {
                                if (snapshotProp.TryGetProperty("trackedFileBackups", out var tfbProp))
                                {
                                    trackedFileBackupsJson = tfbProp.GetRawText();
                                }
                            }

                            // Salva nella tabella file_history_snapshots (NON in messages)
                            SaveFileHistorySnapshot(dbContext, sessionId, timestamp, messageId,
                                trackedFileBackupsJson, isSnapshotUpdate);

                            result.ImportedCount++;
                            Log.Debug("Saved file history snapshot for session {SessionId}: messageId={MessageId}",
                                sessionId, messageId);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to parse file-history-snapshot content at line {LineNumber}: {Content}",
                                lineNumber, content);
                            result.SkippedCount++;
                        }

                        // Skip SaveMessageAsync per i file-history-snapshot
                        progressCallback?.Report((lineNumber, totalLines));
                        continue;
                    }

                    // Check se è un messaggio di tipo "queue-operation" - gestione speciale
                    if (type == "queue-operation")
                    {
                        try
                        {
                            // Parse del content JSON per estrarre i campi necessari
                            var contentJson = JsonDocument.Parse(content);
                            var operation = contentJson.RootElement.TryGetProperty("operation", out var opProp)
                                ? opProp.GetString() ?? ""
                                : "";
                            var queueContent = contentJson.RootElement.TryGetProperty("content", out var contentProp)
                                ? contentProp.GetString() ?? ""
                                : "";

                            // Salva nella tabella queue_operations (NON in messages)
                            SaveQueueOperation(dbContext, sessionId, timestamp, operation, queueContent);

                            result.ImportedCount++;
                            Log.Debug("Saved queue operation for session {SessionId}: operation={Operation}",
                                sessionId, operation);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to parse queue-operation content at line {LineNumber}: {Content}",
                                lineNumber, content);
                            result.SkippedCount++;
                        }

                        // Skip SaveMessageAsync per i queue-operation
                        progressCallback?.Report((lineNumber, totalLines));
                        continue;
                    }

                    if (uuid == null)
                    {
                        Debugger.Break();
                    }

                    // Salva nel DB con TUTTI i metadata (modalità batch: passa existingUuids e dbContext)
                    await SaveMessageAsync(
                        existingUuids,  // HashSet per evitare duplicati
                        dbContext,      // DbContext condiviso per batch
                        sessionId,
                        type ?? "unknown",
                        content,
                        timestamp,
                        uuid,
                        parentUuid,
                        version,
                        gitBranch,
                        isSidechain,
                        userType,
                        cwd,
                        requestId,
                        model,
                        usageJson,
                        type); // messageType = type

                    result.ImportedCount++;

                    // Report progress
                    progressCallback?.Report((lineNumber, totalLines));
                }

                // Salva con diagnostica e transazione automatica
                progressCallback?.Report((lineNumber, totalLines));
                var affectedRows = await dbContext.SaveChangesAsync();


                return result;
            }
            catch (OperationCanceledException)
            {
                Log.Information("Import cancelled by user after {Imported} messages", result.ImportedCount);
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to import messages from {FilePath}", jsonlFilePath);
                throw;
            }
        }

        private async Task<bool> MessageExistsByUuidAsync(string uuid)
        {
            using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            return await dbContext.Messages.AnyAsync(m => m.Uuid == uuid);
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
                                else
                                {
                                    var value = item.GetRawText();
                                    sb.AppendLine(value);
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
