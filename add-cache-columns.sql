-- Script per aggiungere colonne dedicate per cache Anthropic
-- Eseguire su database ClaudeGui

USE ClaudeGui;

-- Aggiungi colonne per i nuovi campi cache di Anthropic
ALTER TABLE messages ADD COLUMN cache_5m_tokens INT NULL
    COMMENT 'Token cache ephemeral 5 minuti (da usage.cache_creation.ephemeral_5m_input_tokens)';

ALTER TABLE messages ADD COLUMN cache_1h_tokens INT NULL
    COMMENT 'Token cache ephemeral 1 ora (da usage.cache_creation.ephemeral_1h_input_tokens)';

ALTER TABLE messages ADD COLUMN service_tier VARCHAR(20) NULL
    COMMENT 'Tier servizio API Anthropic (standard, premium, etc.)';

-- Crea indici per query performance
CREATE INDEX idx_messages_service_tier ON messages(service_tier);
CREATE INDEX idx_messages_cache_5m ON messages(cache_5m_tokens);

-- Mostra struttura aggiornata
DESCRIBE messages;

SELECT 'Script completato con successo! Colonne cache aggiunte.' AS Status;
