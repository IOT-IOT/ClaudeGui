using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ClaudeCodeMAUI.Models;
using ClaudeCodeMAUI.Models.Entities;
using Serilog;

namespace ClaudeCodeMAUI.Services
{
    /// <summary>
    /// Service per scansionare il filesystem alla ricerca di sessioni Claude Code.
    /// Scansiona C:\Users\{user}\.claude\projects\ per trovare tutti i file .jsonl
    /// e sincronizza gli UUID delle sessioni con il database.
    /// </summary>
    public class SessionScannerService
    {
        private readonly DbService _dbService;
        private readonly string _claudeProjectsPath;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="dbService">Service per accesso al database</param>
        public SessionScannerService(DbService dbService)
        {
            _dbService = dbService;

            // Path base dei progetti Claude Code
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _claudeProjectsPath = Path.Combine(userProfile, ".claude", "projects");

            Log.Information("SessionScannerService initialized. Projects path: {Path}", _claudeProjectsPath);
        }

        /// <summary>
        /// Decodifica il nome di una cartella Claude projects in working directory.
        /// Es: "C--Sources-MyProject" → "C:\Sources\MyProject"
        /// Regola: prima occorrenza di "--" diventa ":\", tutte le "-" successive diventano "\"
        /// </summary>
        private string DecodeWorkingDirectory(string encodedName)
        {
            if (string.IsNullOrWhiteSpace(encodedName))
                return string.Empty;

            // Trova la prima occorrenza di "--" e sostituiscila con ":\"
            var index = encodedName.IndexOf("--");
            if (index == -1)
                return encodedName; // Nessun "--" trovato

            var result = encodedName.Substring(0, index) + ":\\" + encodedName.Substring(index + 2);

            // Tutte le "-" rimanenti diventano "\"
            result = result.Replace("-", "\\");

            return result;
        }

        /// <summary>
        /// Controlla se il primo record JSON del file contiene un type da escludere.
        /// Tipi esclusi: "summary", "file-history-snapshot", "queue-operation"
        /// Legge solo la prima riga per massima efficienza.
        /// I file JSONL hanno un record JSON per riga.
        /// </summary>
        /// <param name="jsonlFilePath">Path completo del file .jsonl</param>
        /// <returns>Tupla (isExcluded, excludedReason): (true, "summary") se summary, (true, "file-history-snapshot") se file-history-snapshot, (true, "queue-operation") se queue-operation, (false, null) altrimenti</returns>
        private async Task<(bool isExcluded, string? reason)> CheckIfExcludedSessionAsync(string jsonlFilePath)
        {
            try
            {
                // Leggi solo la PRIMA riga (JSONL = una riga per record)
                using var reader = new StreamReader(jsonlFilePath);
                var firstLine = await reader.ReadLineAsync();

                if (string.IsNullOrWhiteSpace(firstLine))
                {
                    Log.Debug("Empty first line in file: {File}", jsonlFilePath);
                    return (false, null);
                }

                // Parse JSON
                var json = JsonDocument.Parse(firstLine);

                // Controlla se type è da escludere
                if (json.RootElement.TryGetProperty("type", out var typeProperty))
                {
                    var type = typeProperty.GetString();

                    if (type == "summary")
                    {
                        Log.Information("Session excluded (summary): {File}", Path.GetFileName(jsonlFilePath));
                        return (true, "summary");
                    }
                    else if (type == "file-history-snapshot")
                    {
                        Log.Information("Session excluded (file-history-snapshot): {File}", Path.GetFileName(jsonlFilePath));
                        return (true, "file-history-snapshot");
                    }
                    else if (type == "queue-operation")
                    {
                        Log.Information("Session excluded (queue-operation): {File}", Path.GetFileName(jsonlFilePath));
                        return (true, "queue-operation");
                    }
                }

                return (false, null);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to check if session should be excluded: {File}", jsonlFilePath);
                return (false, null); // In caso di errore, non escludere (safe default)
            }
        }

        /// <summary>
        /// Sincronizza il filesystem con il database.
        /// Scansiona tutti i file .jsonl (esclusi agent-*) nel filesystem,
        /// estrae gli UUID dai nomi file e inserisce nel DB le sessioni mancanti.
        /// Per ogni sessione, verifica se è di tipo "summary" o "file-history-snapshot" e la marca come esclusa con il motivo.
        /// Implementa garbage collection: rimuove dal DB le sessioni orfane (file cancellati dal filesystem).
        /// Il filesystem è la single source of truth.
        /// </summary>
        public async Task SyncFilesystemWithDatabaseAsync()
        {
            try
            {
                Log.Information("=== SyncFilesystemWithDatabaseAsync START ===");
                Log.Information("Claude projects path: {Path}", _claudeProjectsPath);

                // Verifica che la cartella progetti esista
                if (!Directory.Exists(_claudeProjectsPath))
                {
                    Log.Warning("Claude projects directory does not exist: {Path}", _claudeProjectsPath);
                    return;
                }

                // Scansiona tutte le sotto-cartelle (ogni cartella = working directory encodata)
                var projectDirs = Directory.GetDirectories(_claudeProjectsPath);
                Log.Information("Found {Count} project directories", projectDirs.Length);

                // MARK PHASE: Lista di tutti i session ID trovati nel filesystem
                var foundSessionIds = new List<string>();

                int totalFilesFound = 0;
                int newSessionsInserted = 0;
                int sessionsProcessed = 0;
                int sessionsExcluded = 0;

                foreach (var projectDir in projectDirs)
                {
                    // Estrai working directory dal nome della cartella
                    var encodedDirName = Path.GetFileName(projectDir);
                    var workingDirectory = DecodeWorkingDirectory(encodedDirName);

                    Log.Debug("Processing directory: {EncodedName} → {WorkingDir}", encodedDirName, workingDirectory);

                    // Trova tutti i file .jsonl (escludendo agent-*.jsonl)
                    var jsonlFiles = Directory.GetFiles(projectDir, "*.jsonl")
                        .Where(f => !Path.GetFileName(f).StartsWith("agent-"))
                        .ToList();

                    totalFilesFound += jsonlFiles.Count;
                    Log.Debug("  Found {Count} session files", jsonlFiles.Count);

                    foreach (var jsonlFile in jsonlFiles)
                    {
                        try
                        {
                            // Estrai UUID dal nome file (es: "e9c7084c-6ec4-4635-996c-03692a8d4507.jsonl" → "e9c7084c-6ec4-4635-996c-03692a8d4507")
                            var sessionId = Path.GetFileNameWithoutExtension(jsonlFile);

                            // MARK: Aggiungi alla lista dei session ID trovati nel filesystem
                            foundSessionIds.Add(sessionId);

                            // Ottieni info del file (data modifica)
                            var fileInfo = new FileInfo(jsonlFile);
                            var lastModified = fileInfo.LastWriteTime;

                            // Ottieni stato dal database (se esiste)
                            var dbSession = await _dbService.GetSessionByIdAsync(sessionId);

                            if (dbSession == null)
                            {
                                // ========== SESSIONE NUOVA ==========
                                // Inserisci nel DB (ancora non processata)
                                await _dbService.InsertSessionAsync(sessionId, "", workingDirectory, lastModified);
                                newSessionsInserted++;
                                Log.Information("  New session discovered: {SessionId}, LastModified: {LastModified}",
                                    sessionId, lastModified);

                                // Processa il file per verificare se deve essere escluso
                                var (isExcluded, excludedReason) = await CheckIfExcludedSessionAsync(jsonlFile);

                                // Aggiorna DB: processato + eventualmente escluso con motivo
                                await _dbService.MarkSessionAsProcessedAsync(sessionId, isExcluded, excludedReason);
                                sessionsProcessed++;

                                if (isExcluded)
                                {
                                    sessionsExcluded++;
                                    Log.Information("  Session marked as excluded ({Reason}): {SessionId}", excludedReason, sessionId);
                                }
                            }
                            else if (!dbSession.Processed)
                            {
                                // ========== SESSIONE ESISTENTE MA NON PROCESSATA ==========
                                var (isExcluded, excludedReason) = await CheckIfExcludedSessionAsync(jsonlFile);
                                await _dbService.MarkSessionAsProcessedAsync(sessionId, isExcluded, excludedReason);
                                sessionsProcessed++;

                                if (isExcluded)
                                {
                                    sessionsExcluded++;
                                    Log.Information("  Session marked as excluded ({Reason}): {SessionId}", excludedReason, sessionId);
                                }
                            }
                            else if (dbSession.Excluded)
                            {
                                // ========== SESSIONE ESCLUSA ==========
                                //Log.Debug("  Skipping excluded session: {SessionId}", sessionId);
                            }
                            else
                            {
                                // ========== SESSIONE VALIDA GIÀ PROCESSATA ==========
                                //Log.Debug("  Session already processed and valid: {SessionId}", sessionId);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to process session file: {File}", jsonlFile);
                        }
                    }
                }

                Log.Information("=== Sync completed: {TotalFiles} files found, {NewSessions} new, {Processed} processed, {Excluded} excluded ===",
                    totalFilesFound, newSessionsInserted, sessionsProcessed, sessionsExcluded);

                // SWEEP PHASE: Garbage collection - rimuovi sessioni orfane dal database
                Log.Information("=== Starting Garbage Collection ===");
                Log.Information("Found {Count} valid sessions in filesystem", foundSessionIds.Count);

                int orphanedCount = 0;
                if (foundSessionIds.Count > 0)
                {
                    orphanedCount = await _dbService.RemoveOrphanedSessionsAsync(foundSessionIds);
                }
                else
                {
                    Log.Warning("No sessions found in filesystem - skipping garbage collection to prevent accidental deletion");
                }

                Log.Information("=== SyncFilesystemWithDatabaseAsync COMPLETED ===");
                Log.Information("Summary: {TotalFiles} files, {NewSessions} new, {Processed} processed, {Excluded} excluded, {Orphaned} orphaned removed",
                    totalFilesFound, newSessionsInserted, sessionsProcessed, sessionsExcluded, orphanedCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to sync filesystem with database");
            }
        }


        /// <summary>
        /// Ottiene le sessioni con status = 'open' dal database.
        /// Utilizzato al boot per riaprire le sessioni che erano aperte.
        /// </summary>
        /// <returns>Lista di entity Session aperte dal DB</returns>
        public async Task<List<Session>> GetOpenSessionsAsync()
        {
            return await _dbService.GetOpenSessionsAsync();
        }

        /// <summary>
        /// Ottiene le sessioni con status = 'closed' dal database.
        /// Utilizzato per popolare la lista nel SessionSelectorPage.
        /// </summary>
        /// <returns>Lista di entity Session chiuse dal DB</returns>
        public async Task<List<Session>> GetClosedSessionsAsync()
        {
            return await _dbService.GetClosedSessionsAsync();
        }
    }
}
