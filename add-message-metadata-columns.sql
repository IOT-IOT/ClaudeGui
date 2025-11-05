-- Script per aggiungere colonne metadata alla tabella messages
-- Eseguire su database ClaudeGui

USE ClaudeGui;

-- Aggiungi colonne per metadati completi dei messaggi
ALTER TABLE messages ADD COLUMN uuid VARCHAR(36) NULL COMMENT 'UUID univoco del messaggio dal file .jsonl';
ALTER TABLE messages ADD COLUMN parent_uuid VARCHAR(36) NULL COMMENT 'UUID del messaggio genitore (thread chain)';
ALTER TABLE messages ADD COLUMN version VARCHAR(20) NULL COMMENT 'Versione Claude Code (es: 2.0.29)';
ALTER TABLE messages ADD COLUMN git_branch VARCHAR(255) NULL COMMENT 'Branch git attivo al momento del messaggio';
ALTER TABLE messages ADD COLUMN is_sidechain BOOLEAN DEFAULT FALSE COMMENT 'Indica se messaggio fa parte di una sidechain';
ALTER TABLE messages ADD COLUMN user_type VARCHAR(20) NULL COMMENT 'Tipo utente (external, system, etc.)';
ALTER TABLE messages ADD COLUMN cwd VARCHAR(500) NULL COMMENT 'Working directory al momento del messaggio';
ALTER TABLE messages ADD COLUMN request_id VARCHAR(50) NULL COMMENT 'ID richiesta API (solo assistant)';
ALTER TABLE messages ADD COLUMN model VARCHAR(100) NULL COMMENT 'Modello Claude usato (solo assistant)';
ALTER TABLE messages ADD COLUMN usage_json TEXT NULL COMMENT 'Statistiche uso tokens (JSON)';
ALTER TABLE messages ADD COLUMN message_type VARCHAR(50) NULL COMMENT 'Tipo messaggio originale (user, assistant, tool_use, tool_result, summary, etc.)';

-- Crea indice univoco per uuid (previene duplicati)
CREATE UNIQUE INDEX idx_messages_uuid ON messages(uuid);

-- Mostra struttura aggiornata
DESCRIBE messages;

SELECT 'Script completato con successo!' AS Status;
