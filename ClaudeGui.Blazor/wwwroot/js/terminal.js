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
        hubConnection.on('ReceiveOutput', (connectionId, rawOutput) => {
            console.log('[ClaudeTerminal] ReceiveOutput - connectionId:', connectionId, 'rawOutput length:', rawOutput?.length);
            handleOutputReceived(connectionId, rawOutput);
        });

        // ✅ SessionIdDetected handler: riceve il Session ID di Claude quando rilevato
        hubConnection.on('SessionIdDetected', (connectionId, claudeSessionId) => {
            console.log('[ClaudeTerminal] ✅ Claude Session ID detected for terminal:', connectionId, 'sessionId:', claudeSessionId);
            handleSessionIdDetected(connectionId, claudeSessionId);
        });

        hubConnection.on('ReceiveError', (error) => {
            console.error('[ClaudeTerminal] ReceiveError:', error);
            handleErrorReceived(error);
        });

        hubConnection.on('FatalError', (error, sessionId) => {
            console.error('[ClaudeTerminal] FatalError:', error, 'SessionId:', sessionId);
            handleFatalError(error, sessionId);
        });

        hubConnection.on('ProcessCompleted', (connectionId, exitCode, wasKilled) => {
            console.log('[ClaudeTerminal] ProcessCompleted - ConnectionId:', connectionId, 'ExitCode:', exitCode, 'WasKilled:', wasKilled);
            handleProcessCompleted(connectionId, exitCode, wasKilled);
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
     * @param {string} connectionId - Connection ID univoco del terminal (per routing)
     * @param {string} rawOutput - Raw output ricevuto da Claude
     */
    function handleOutputReceived(connectionId, rawOutput) {
        try {
            console.log('[ClaudeTerminal] handleOutputReceived - connectionId type:', typeof connectionId, 'value:', connectionId);
            console.log('[ClaudeTerminal] handleOutputReceived - rawOutput type:', typeof rawOutput, 'value:', rawOutput?.substring(0, 50));

            // In modalità interactive, scriviamo direttamente l'output a xterm.js
            // xterm.js gestisce automaticamente ANSI escape codes, colori, ecc.

            // ✅ ROUTING CORRETTO: Invia output SOLO al terminal specifico
            const terminalData = terminals.get(connectionId);
            if (terminalData) {
                terminalData.terminal.write(rawOutput);
            } else {
                console.warn('[ClaudeTerminal] ⚠️ Output ricevuto per terminal sconosciuto:', connectionId);
                console.log('[ClaudeTerminal] Terminals disponibili:', Array.from(terminals.keys()));
            }
        } catch (err) {
            console.error('[ClaudeTerminal] Errore writing output:', err);
        }
    }

    /**
     * ✅ Handler per Session ID di Claude rilevato dal backend.
     * Chiamato quando il backend rileva il Session ID dall'output di /status.
     * Notifica il componente Blazor per aggiornare la UI.
     * @param {string} connectionId - Connection ID del terminal
     * @param {string} claudeSessionId - UUID del Session ID di Claude
     */
    function handleSessionIdDetected(connectionId, claudeSessionId) {
        try {
            console.log('[ClaudeTerminal] 🎯 Session ID detected for terminal:', connectionId, 'sessionId:', claudeSessionId);

            // ✅ ROUTING CORRETTO: Notifica SOLO il terminal specifico
            const terminalData = terminals.get(connectionId);
            if (terminalData && terminalData.dotNetRef) {
                console.log('[ClaudeTerminal] Calling Blazor OnSessionIdDetected for terminal:', connectionId);
                terminalData.dotNetRef.invokeMethodAsync('OnSessionIdDetected', claudeSessionId)
                    .then(() => {
                        console.log('[ClaudeTerminal] ✅ Blazor UI updated with Session ID');
                    })
                    .catch(err => {
                        console.error('[ClaudeTerminal] Error calling Blazor:', err);
                    });
            } else {
                console.warn('[ClaudeTerminal] ⚠️ SessionIdDetected per terminal sconosciuto o senza dotNetRef:', connectionId);
            }
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
     * @param {string} connectionId - Connection ID del terminal
     * @param {number} exitCode - Exit code del processo
     * @param {boolean} wasKilled - True se processo terminato forzatamente
     */
    function handleProcessCompleted(connectionId, exitCode, wasKilled) {
        console.log('[ClaudeTerminal] handleProcessCompleted - routing to terminal:', connectionId);

        // ✅ ROUTING CORRETTO: Invia evento SOLO al terminal specifico
        const terminalData = terminals.get(connectionId);
        if (terminalData) {
            const message = wasKilled
                ? '\r\n\x1b[33m[Processo terminato forzatamente]\x1b[0m\r\n'
                : `\r\n\x1b[32m[Processo completato - Exit code: ${exitCode}]\x1b[0m\r\n`;
            terminalData.terminal.write(message);

            // Invoke Blazor callback to notify Terminal.razor
            if (terminalData.dotNetRef) {
                terminalData.dotNetRef.invokeMethodAsync('OnProcessCompletedCallback')
                    .catch(err => console.error('[ClaudeTerminal] Error invoking OnProcessCompletedCallback:', err));
            }
        } else {
            console.warn('[ClaudeTerminal] ⚠️ ProcessCompleted per terminal sconosciuto:', connectionId);
            console.log('[ClaudeTerminal] Terminals disponibili:', Array.from(terminals.keys()));
        }
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
     * @param {boolean} runAsAdmin - True per eseguire claude.exe con privilegi amministratore (UAC)
     * @returns {Promise<string>} - Session ID creato/riutilizzato
     */
    async function init(elementId, sessionId, workingDirectory, sessionName, dotNetRef, runAsAdmin = false) {
        console.log('[ClaudeTerminal] init() called:', { elementId, sessionId, workingDirectory, sessionName, hasDotNetRef: !!dotNetRef, runAsAdmin });

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
                scrollback: 10000, // Buffer per scrollback - permette di scrollare fino a 10000 righe precedenti
                convertEol: false, // Mantieni gestione EOL del PTY
                allowTransparency: false
            });

            // Crea FitAddon per adattare automaticamente il terminale al container
            const fitAddon = new FitAddon.FitAddon();
            terminal.loadAddon(fitAddon);

            // Apri terminal nell'elemento DOM
            terminal.open(terminalElement);

            // Adatta le dimensioni del terminale al container
            // Timeout per dare tempo al DOM di renderizzare
            setTimeout(() => {
                try {
                    fitAddon.fit();
                    console.log('[ClaudeTerminal] Terminal fitted to container - rows:', terminal.rows, 'cols:', terminal.cols);
                } catch (err) {
                    console.error('[ClaudeTerminal] Error fitting terminal:', err);
                }
            }, 100);

            // 3. Genera connectionId univoco per questo terminal
            // ⚠️ IMPORTANTE: In Blazor Server, tutti i component condividono lo stesso Context.ConnectionId!
            // Usiamo elementId come connectionId univoco per distinguere i terminal.
            const uniqueConnectionId = elementId;
            console.log('[ClaudeTerminal] Using unique connectionId:', uniqueConnectionId);

            // 4. Gestione input utente (onData = input da tastiera)
            // Con PTY, inviamo OGNI carattere immediatamente al backend.
            // Il PTY gestisce echo, line buffering, e tutto il resto come un vero terminal.
            // ⚡ IMPORTANTE: NO await - fire-and-forget per evitare serializzazione dei caratteri
            terminal.onData((data) => {
                // ✅ Fire-and-forget: invia IMMEDIATAMENTE senza aspettare risposta
                // ✅ Ogni carattere viene inviato in parallelo, senza bloccare i successivi
                // ✅ NO await - altrimenti i caratteri vengono serializzati e arrivano in ritardo
                hubConnection.invoke('SendInput', uniqueConnectionId, data).catch(err => {
                    console.error('[ClaudeTerminal] Errore invio input:', err);
                    terminal.write(`\r\n\x1b[31m[Errore invio: ${err.message}]\x1b[0m\r\n`);
                });
            });

            // 4. ⚠️ IMPORTANTE: Aggiungi terminal alla mappa SUBITO con ID temporaneo
            // Questo permette a handleOutputReceived di trovare il terminal mentre CreateSession è in attesa
            const tempId = elementId; // Usa elementId come chiave temporanea
            terminals.set(tempId, {
                terminal: terminal,
                fitAddon: fitAddon, // Salva fitAddon per resize
                elementId: elementId,
                sessionId: null, // Verrà aggiornato dopo
                dotNetRef: dotNetRef // Riferimento per callback a Blazor
            });
            console.log('[ClaudeTerminal] Terminal registered with temp ID:', tempId);

            // 5. Chiama CreateSession con connectionId univoco
            // Durante l'attesa, il terminal riceve già output tramite handleOutputReceived
            console.log('[ClaudeTerminal] Calling CreateSession with unique connectionId:', uniqueConnectionId, 'runAsAdmin:', runAsAdmin);
            const returnedConnectionId = await hubConnection.invoke('CreateSession', workingDirectory, sessionId, sessionName, uniqueConnectionId, runAsAdmin);
            console.log('[ClaudeTerminal] ✅ CreateSession returned connectionId:', returnedConnectionId);

            // 6. Aggiorna mappa con ConnectionId reale
            const terminalData = terminals.get(tempId);
            terminals.delete(tempId); // Rimuovi entry temporanea
            terminals.set(returnedConnectionId, {
                ...terminalData,
                sessionId: returnedConnectionId,
                dotNetRef: dotNetRef // Preserva riferimento .NET
            });
            console.log('[ClaudeTerminal] Terminal remapped:', tempId, '→', returnedConnectionId);

            // 7. Registra handler per resize (ora abbiamo il ConnectionId)
            terminal.onResize(({ cols, rows }) => {
                console.log('[ClaudeTerminal] Terminal resized:', { cols, rows });
                hubConnection.invoke('ResizeTerminal', returnedConnectionId, cols, rows)
                    .catch(err => console.error('[ClaudeTerminal] Error sending resize:', err));
            });

            // 8. Invia dimensioni iniziali al PTY backend dopo fit()
            setTimeout(() => {
                console.log('[ClaudeTerminal] Sending initial terminal size to PTY:', terminal.rows, 'x', terminal.cols);
                hubConnection.invoke('ResizeTerminal', returnedConnectionId, terminal.cols, terminal.rows)
                    .catch(err => console.error('[ClaudeTerminal] Error sending initial resize:', err));
            }, 200);

            // 9. Listener per window resize con debouncing
            let resizeTimeout;
            const handleWindowResize = () => {
                clearTimeout(resizeTimeout);
                resizeTimeout = setTimeout(() => {
                    try {
                        fitAddon.fit();
                        console.log('[ClaudeTerminal] Window resized, terminal refitted to:', terminal.rows, 'x', terminal.cols);
                    } catch (err) {
                        console.error('[ClaudeTerminal] Error refitting on window resize:', err);
                    }
                }, 300); // Debounce 300ms
            };

            window.addEventListener('resize', handleWindowResize);

            // Salva handler per cleanup
            const updatedData = terminals.get(returnedConnectionId);
            terminals.set(returnedConnectionId, {
                ...updatedData,
                resizeHandler: handleWindowResize,
                resizeTimeout: resizeTimeout
            });

            return returnedConnectionId;

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

            // 2. Rimuovi listener resize e cancella timeout
            if (terminalData.resizeHandler) {
                window.removeEventListener('resize', terminalData.resizeHandler);
                console.log('[ClaudeTerminal] Resize handler removed');
            }
            if (terminalData.resizeTimeout) {
                clearTimeout(terminalData.resizeTimeout);
            }

            // 3. Chiudi terminal xterm.js
            terminalData.terminal.dispose();

            // 4. Rimuovi dalla mappa
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

    /**
     * Apre un modal Bootstrap tramite JavaScript.
     * @param {string} modalId - L'ID del modal da aprire (es. "newSessionModal")
     */
    function openModal(modalId) {
        try {
            const modalElement = document.getElementById(modalId);
            if (!modalElement) {
                console.error(`[ClaudeTerminal] Modal con ID '${modalId}' non trovato`);
                return;
            }

            // Usa Bootstrap 5 API per aprire il modal
            const modal = new bootstrap.Modal(modalElement);
            modal.show();
            console.log(`[ClaudeTerminal] Modal '${modalId}' aperto`);
        } catch (err) {
            console.error('[ClaudeTerminal] Errore apertura modal:', err);
        }
    }

    /**
     * Chiude un modal Bootstrap tramite JavaScript.
     * @param {string} modalId - L'ID del modal da chiudere (es. "newSessionModal")
     */
    function closeModal(modalId) {
        try {
            const modalElement = document.getElementById(modalId);
            if (!modalElement) {
                console.error(`[ClaudeTerminal] Modal con ID '${modalId}' non trovato`);
                return;
            }

            // Usa Bootstrap 5 API per chiudere il modal
            const modalInstance = bootstrap.Modal.getInstance(modalElement);
            if (modalInstance) {
                modalInstance.hide();
                console.log(`[ClaudeTerminal] Modal '${modalId}' chiuso`);
            }
        } catch (err) {
            console.error('[ClaudeTerminal] Errore chiusura modal:', err);
        }
    }

    // API pubblica
    return {
        init: init,
        dispose: dispose,
        getSessionInfo: getSessionInfo,
        getActiveSessions: getActiveSessions,
        pickDirectory: pickDirectory,
        openModal: openModal,
        closeModal: closeModal
    };
})();
