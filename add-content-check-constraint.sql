-- Script per aggiungere constraint CHECK sul campo content
-- Impedisce stringhe vuote nella tabella messages
-- Database: ClaudeGui
-- Data: 2025-01-06

USE ClaudeGui;

-- Aggiungi constraint per impedire content vuoto
-- Il campo è già NOT NULL, questo aggiunge controllo per stringhe vuote
ALTER TABLE messages
ADD CONSTRAINT chk_content_not_empty CHECK (LENGTH(content) > 0);

-- Verifica che il constraint sia stato aggiunto
SELECT
    CONSTRAINT_NAME,
    CHECK_CLAUSE
FROM information_schema.CHECK_CONSTRAINTS
WHERE CONSTRAINT_SCHEMA = 'ClaudeGui'
  AND TABLE_NAME = 'messages';
