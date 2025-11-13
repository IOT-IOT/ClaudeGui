/**
 * ClaudeGui Terminal JavaScript Interop
 *
 * Gestisce l'integrazione tra xterm.js e SignalR per la comunicazione
 * bidirezionale con il backend ClaudeHub.
 *
 * Dipendenze:
 * - xterm.js (terminal emulator)
 * - @microsoft/signalr (SignalR client)
 *
 * Uso da Blazor:
 * await JSRuntime.InvokeVoidAsync("ClaudeTerminal.init", terminalElementId, sessionId, workingDirectory);
 */

window.ClaudeTerminal = (function () {
    'use strict';

    // Mappa dei terminal attivi per sessionId
    const terminals = new Map();

    // SignalR connection (shared tra tutti i terminal)
    let hubConnection = null;

    /**
     * Inizializza la connessione SignalR al ClaudeHub.
     * Viene chiamata automaticamente da init() se non già connessa.
     */
    async function initializeSignalR() {
        if (hubConnection && hubConnection.state === signalR.HubConnectionState.Connected) {
            console.log('[ClaudeTerminal] SignalR già connesso');
            return;
        }

        console.log('[ClaudeTerminal] Inizializzazione SignalR...');

        hubConnection = new signalR.HubConnectionBuilder()
            .withUrl('/claudehub')
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Information)
            .build();

        // Event handlers per messaggi dal server
        hubConnection.on('ReceiveOutput', (rawOutput) => {
            console.log('[ClaudeTerminal] ReceiveOutput:', rawOutput);
            handleOutputReceived(rawOutput);
        });

        // ✅ SessionIdDetected handler: riceve il Session ID di Claude quando rilevato
        hubConnection.on('SessionIdDetected', (claudeSessionId) => {
            console.log('[ClaudeTerminal] ✅ Claude Session ID detected:', claudeSessionId);
            handleSessionIdDetected(claudeSessionId);
        });

        hubConnection.on('ReceiveError', (error) => {
            console.error('[ClaudeTerminal] ReceiveError:', error);
            handleErrorReceived(error);
        });

        hubConnection.on('FatalError', (error, sessionId) => {
            console.error('[ClaudeTerminal] FatalError:', error, 'SessionId:', sessionId);
            handleFatalError(error, sessionId);
        });

        hubConnection.on('ProcessCompleted', (exitCode, wasKilled) => {
            console.log('[ClaudeTerminal] ProcessCompleted - ExitCode:', exitCode, 'WasKilled:', wasKilled);
            handleProcessCompleted(exitCode, wasKilled);
        });

        hubConnection.on('SessionTerminated', (sessionId) => {
            console.log('[ClaudeTerminal] SessionTerminated:', sessionId);
            handleSessionTerminated(sessionId);
        });

        // Reconnection handlers
        hubConnection.onreconnecting((error) => {
            console.warn('[ClaudeTerminal] SignalR reconnecting...', error);
            // Mostra indicator di reconnection su tutti i terminal
            terminals.forEach((terminalData) => {
                terminalData.terminal.write('\r\n[Riconnessione in corso...]\r\n');
            });
        });

        hubConnection.onreconnected((connectionId) => {
            console.log('[ClaudeTerminal] SignalR reconnected:', connectionId);
            terminals.forEach((terminalData) => {
                terminalData.terminal.write('[Riconnesso]\r\n');
            });
        });

        hubConnection.onclose((error) => {
            console.error('[ClaudeTerminal] SignalR disconnected:', error);
            terminals.forEach((terminalData) => {
                terminalData.terminal.write('\r\n[Connessione persa]\r\n');
            });
        });

        try {
            await hubConnection.start();
            console.log('[ClaudeTerminal] SignalR connesso con successo');
        } catch (err) {
            console.error('[ClaudeTerminal] Errore connessione SignalR:', err);
            throw err;
        }
    }

    /**
     * Handler per output ricevuto dal server (raw output da Claude stdout).
     * In modalità interactive, Claude emette output normale con ANSI escape codes.
     * @param {string} rawOutput - Raw output ricevuto da Claude
     */
    function handleOutputReceived(rawOutput) {
        try {
            // In modalità interactive, scriviamo direttamente l'output a xterm.js
            // xterm.js gestisce automaticamente ANSI escape codes, colori, ecc.

            // Trova il terminal corretto (broadcast a tutti per ora, migliora con sessionId se necessario)
            terminals.forEach((terminalData) => {
                terminalData.terminal.write(rawOutput);
            });
        } catch (err) {
            console.error('[ClaudeTerminal] Errore writing output:', err);
        }
    }

    /**
     * ✅ Handler per Session ID di Claude rilevato dal backend.
     * Chiamato quando il backend rileva il Session ID dall'output di /status.
     * Notifica il componente Blazor per aggiornare la UI.
     * @param {string} claudeSessionId - UUID del Session ID di Claude
     */
    function handleSessionIdDetected(claudeSessionId) {
        try {
            console.log('[ClaudeTerminal] 🎯 Handling Claude Session ID:', claudeSessionId);

            // Trova il terminal che ha questo Session ID e notifica il componente Blazor
            terminals.forEach((terminalData, connectionId) => {
                if (terminalData.sessionId === connectionId) {
                    // Notifica Blazor component se ha un riferimento .NET
                    if (terminalData.dotNetRef) {
                        console.log('[ClaudeTerminal] Calling Blazor OnSessionIdDetected...');
                        terminalData.dotNetRef.invokeMethodAsync('OnSessionIdDetected', claudeSessionId)
                            .then(() => {
                                console.log('[ClaudeTerminal] ✅ Blazor UI updated with Session ID');
                            })
                            .catch(err => {
                                console.error('[ClaudeTerminal] Error calling Blazor:', err);
                            });
                    } else {
                        console.warn('[ClaudeTerminal] No dotNetRef found for terminal:', connectionId);
                    }
                }
            });
        } catch (err) {
            console.error('[ClaudeTerminal] Error handling SessionIdDetected:', err);
        }
    }

    /**
     * Handler per errori ricevuti dal server.
     * @param {string} error - Messaggio di errore
     */
    function handleErrorReceived(error) {
        // Mostra errore su tutti i terminal (o su quello specifico se abbiamo sessionId)
        terminals.forEach((terminalData) => {
            terminalData.terminal.write(`\r\n\x1b[31m[Errore: ${error}]\x1b[0m\r\n`);
        });
    }

    /**
     * Handler per errori fatali che richiedono chiusura terminal.
     * @param {string} error - Messaggio di errore
     * @param {string} sessionId - ID della sessione da chiudere (ConnectionId)
     */
    function handleFatalError(error, sessionId) {
        console.error('[ClaudeTerminal] Fatal error - closing terminal:', error, 'SessionId:', sessionId);

        // Trova il terminal corrispondente
        const terminalData = terminals.get(sessionId);
        if (!terminalData) {
            console.error('[ClaudeTerminal] Terminal not found for sessionId:', sessionId, 'Available keys:', Array.from(terminals.keys()));
            return;
        }

        // Mostra errore fatale nel terminal
        terminalData.terminal.write(`\r\n\x1b[41;1m[ERRORE FATALE]\x1b[0m\x1b[31m ${error}\x1b[0m\r\n`);
        terminalData.terminal.write(`\x1b[33mIl terminal verrà chiuso tra 3 secondi...\x1b[0m\r\n`);

        // Attendi 3 secondi poi chiudi il terminal e notifica Blazor
        setTimeout(async () => {
            try {
                // Dispose terminal xterm.js
                terminalData.terminal.dispose();

                // Rimuovi dalla mappa
                terminals.delete(sessionId);

                console.log('[ClaudeTerminal] Terminal closed due to fatal error');

                // Ritorna alla homepage (lista sessioni)
                window.location.href = '/';
            } catch (err) {
                console.error('[ClaudeTerminal] Error during fatal error cleanup:', err);
            }
        }, 3000);
    }

    /**
     * Handler per completamento processo Claude.
     * @param {number} exitCode - Exit code del processo
     * @param {boolean} wasKilled - True se processo terminato forzatamente
     */
    function handleProcessCompleted(exitCode, wasKilled) {
        terminals.forEach((terminalData) => {
            const message = wasKilled
                ? '\r\n\x1b[33m[Processo terminato forzatamente]\x1b[0m\r\n'
                : `\r\n\x1b[32m[Processo completato - Exit code: ${exitCode}]\x1b[0m\r\n`;
            terminalData.terminal.write(message);
        });
    }

    /**
     * Handler per sessione terminata dal server.
     * @param {string} sessionId - ID della sessione terminata
     */
    function handleSessionTerminated(sessionId) {
        const terminalData = terminals.get(sessionId);
        if (terminalData) {
            terminalData.terminal.write('\r\n\x1b[31m[Sessione terminata dal server]\x1b[0m\r\n');
            // Non rimuoviamo il terminal dalla mappa, lasciamo che sia dispose() a farlo
        }
    }

    /**
     * Inizializza un nuovo terminal xterm.js e lo collega a una sessione Claude.
     *
     * @param {string} elementId - ID dell'elemento HTML dove montare il terminal
     * @param {string} sessionId - ID sessione esistente (null per nuova sessione)
     * @param {string} workingDirectory - Working directory per Claude
     * @param {string} sessionName - Nome della sessione (opzionale, per nuove sessioni)
     * @param {object} dotNetRef - Riferimento .NET per callback a Blazor
     * @returns {Promise<string>} - Session ID creato/riutilizzato
     */
    async function init(elementId, sessionId, workingDirectory, sessionName, dotNetRef) {
        console.log('[ClaudeTerminal] init() called:', { elementId, sessionId, workingDirectory, sessionName, hasDotNetRef: !!dotNetRef });

        try {
            // 1. Inizializza SignalR se necessario
            await initializeSignalR();

            // 2. Crea terminal xterm.js PRIMA di chiamare CreateSession
            // In questo modo è pronto a ricevere output immediatamente
            const terminalElement = document.getElementById(elementId);
            if (!terminalElement) {
                throw new Error(`Elemento con ID '${elementId}' non trovato`);
            }

            const terminal = new Terminal({
                cursorBlink: true,
                fontSize: 14,
                fontFamily: 'Consolas, "Courier New", monospace',
                theme: {
                    background: '#1e1e1e',
                    foreground: '#d4d4d4',
                    cursor: '#ffffff',
                    black: '#000000',
                    red: '#cd3131',
                    green: '#0dbc79',
                    yellow: '#e5e510',
                    blue: '#2472c8',
                    magenta: '#bc3fbc',
                    cyan: '#11a8cd',
                    white: '#e5e5e5',
                    brightBlack: '#666666',
                    brightRed: '#f14c4c',
                    brightGreen: '#23d18b',
                    brightYellow: '#f5f543',
                    brightBlue: '#3b8eea',
                    brightMagenta: '#d670d6',
                    brightCyan: '#29b8db',
                    brightWhite: '#e5e5e5'
                },
                rows: 30,
                cols: 120
            });

            // Apri terminal nell'elemento DOM
            terminal.open(terminalElement);

            // 3. Gestione input utente (onData = input da tastiera)
            // Con PTY, inviamo OGNI carattere immediatamente al backend.
            // Il PTY gestisce echo, line buffering, e tutto il resto come un vero terminal.
            // SignalR usa automaticamente ConnectionId per trovare la sessione.
            // ⚡ IMPORTANTE: NO await - fire-and-forget per evitare serializzazione dei caratteri
            terminal.onData((data) => {
                // ✅ Fire-and-forget: invia IMMEDIATAMENTE senza aspettare risposta
                // ✅ Ogni carattere viene inviato in parallelo, senza bloccare i successivi
                // ✅ NO await - altrimenti i caratteri vengono serializzati e arrivano in ritardo
                hubConnection.invoke('SendInput', data).catch(err => {
                    console.error('[ClaudeTerminal] Errore invio input:', err);
                    terminal.write(`\r\n\x1b[31m[Errore invio: ${err.message}]\x1b[0m\r\n`);
                });
            });

            // 4. ⚠️ IMPORTANTE: Aggiungi terminal alla mappa SUBITO con ID temporaneo
            // Questo permette a handleOutputReceived di trovare il terminal mentre CreateSession è in attesa
            const tempId = elementId; // Usa elementId come chiave temporanea
            terminals.set(tempId, {
                terminal: terminal,
                elementId: elementId,
                sessionId: null, // Verrà aggiornato dopo
                dotNetRef: dotNetRef // Riferimento per callback a Blazor
            });
            console.log('[ClaudeTerminal] Terminal registered with temp ID:', tempId);

            // 5. Chiama CreateSession - ASPETTA il Session ID reale di Claude
            // Durante l'attesa, il terminal riceve già output tramite handleOutputReceived
            console.log('[ClaudeTerminal] Calling CreateSession (will wait for real Session ID)...');
            const realSessionId = await hubConnection.invoke('CreateSession', workingDirectory, sessionId, sessionName);
            console.log('[ClaudeTerminal] ✅ Received real Session ID:', realSessionId);

            // 6. Aggiorna mappa con Session ID reale (ConnectionId)
            const terminalData = terminals.get(tempId);
            terminals.delete(tempId); // Rimuovi entry temporanea
            terminals.set(realSessionId, {
                ...terminalData,
                sessionId: realSessionId,
                dotNetRef: dotNetRef // Preserva riferimento .NET
            });
            console.log('[ClaudeTerminal] Terminal remapped:', tempId, '→', realSessionId);

            // 7. Registra handler per resize (ora abbiamo il Session ID reale)
            terminal.onResize(({ cols, rows }) => {
                console.log('[ClaudeTerminal] Terminal resized:', { cols, rows });
                hubConnection.invoke('ResizeTerminal', realSessionId, cols, rows)
                    .catch(err => console.error('[ClaudeTerminal] Error sending resize:', err));
            });

            return realSessionId;

        } catch (err) {
            console.error('[ClaudeTerminal] Errore init():', err);
            throw err;
        }
    }

    /**
     * Termina una sessione terminal e pulisce le risorse.
     *
     * @param {string} sessionId - ID della sessione da terminare
     */
    async function dispose(sessionId) {
        console.log('[ClaudeTerminal] dispose() called:', sessionId);

        const terminalData = terminals.get(sessionId);
        if (!terminalData) {
            console.warn('[ClaudeTerminal] Terminal non trovato per dispose:', sessionId);
            return;
        }

        try {
            // 1. Termina sessione sul server
            await hubConnection.invoke('KillSession', sessionId);

            // 2. Chiudi terminal xterm.js
            terminalData.terminal.dispose();

            // 3. Rimuovi dalla mappa
            terminals.delete(sessionId);

            console.log('[ClaudeTerminal] Terminal disposed:', sessionId);
        } catch (err) {
            console.error('[ClaudeTerminal] Errore dispose():', err);
        }
    }

    /**
     * Ottiene informazioni su una sessione.
     *
     * @param {string} sessionId - ID della sessione
     * @returns {Promise<object>} - Informazioni sessione
     */
    async function getSessionInfo(sessionId) {
        try {
            return await hubConnection.invoke('GetSessionInfo', sessionId);
        } catch (err) {
            console.error('[ClaudeTerminal] Errore getSessionInfo():', err);
            throw err;
        }
    }

    /**
     * Ottiene lista di tutte le sessioni attive sul server.
     *
     * @returns {Promise<string[]>} - Array di session ID attivi
     */
    async function getActiveSessions() {
        try {
            return await hubConnection.invoke('GetActiveSessions');
        } catch (err) {
            console.error('[ClaudeTerminal] Errore getActiveSessions():', err);
            throw err;
        }
    }

    /**
     * Apre il directory picker nativo del sistema operativo (Chrome/Edge only).
     * Fallback graceful per browser non supportati (Firefox/Safari).
     *
     * @returns {Promise<string|null>} - Path della directory selezionata o null se annullato/non supportato
     */
    async function pickDirectory() {
        try {
            // Verifica se l'API File System Access è supportata (Chrome 86+, Edge 86+)
            if (!window.showDirectoryPicker) {
                console.warn('[ClaudeTerminal] showDirectoryPicker non supportato in questo browser');
                return null;
            }

            // Apre il dialog nativo del sistema operativo
            const dirHandle = await window.showDirectoryPicker({
                mode: 'read', // Solo lettura (non serve write permission)
                startIn: 'documents' // Inizia dalla cartella Documents
            });

            // Ottieni il path completo (se possibile)
            // NOTA: File System Access API non espone il path completo per motivi di sicurezza
            // Restituiamo solo il nome della directory selezionata
            // L'utente dovrà digitare il path completo manualmente se necessario

            // ⚠️ WORKAROUND: Purtroppo l'API non espone il path completo
            // Possiamo solo ottenere il nome della cartella
            console.log('[ClaudeTerminal] Directory selezionata:', dirHandle.name);

            // Mostra messaggio all'utente che deve digitare il path completo
            alert(`Directory selezionata: ${dirHandle.name}\n\nATTENZIONE: Per motivi di sicurezza del browser, devi digitare manualmente il path completo (es. C:\\Users\\enrico\\${dirHandle.name})`);

            return dirHandle.name;

        } catch (err) {
            // Utente ha premuto Cancel o errore permessi
            if (err.name === 'AbortError') {
                console.log('[ClaudeTerminal] Directory picker annullato dall\'utente');
            } else {
                console.error('[ClaudeTerminal] Errore directory picker:', err);
            }
            return null;
        }
    }

    // API pubblica
    return {
        init: init,
        dispose: dispose,
        getSessionInfo: getSessionInfo,
        getActiveSessions: getActiveSessions,
        pickDirectory: pickDirectory
    };
})();
