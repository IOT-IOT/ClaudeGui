-- Script per rimuovere le colonne cache dalla tabella messages
-- Database: ClaudeGui
-- Data: 2025-11-06

USE ClaudeGui;

-- Rimuovi gli indici prima di eliminare le colonne
DROP INDEX IF EXISTS idx_messages_service_tier ON messages;
DROP INDEX IF EXISTS idx_messages_cache_5m ON messages;

-- Rimuovi le colonne cache
ALTER TABLE messages DROP COLUMN IF EXISTS cache_5m_tokens;
ALTER TABLE messages DROP COLUMN IF EXISTS cache_1h_tokens;
ALTER TABLE messages DROP COLUMN IF EXISTS service_tier;

-- Verifica le modifiche
SHOW COLUMNS FROM messages LIKE '%cache%';
SHOW COLUMNS FROM messages LIKE 'service_tier';
