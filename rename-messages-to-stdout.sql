-- ============================================================================
-- Migration: Rename messages table to messages_from_stdout
-- Date: 2025-11-12
-- Purpose: Cleanup after splitting SaveMessageFromJson methods
--          Remove obsolete fields: parentUuid, gitBranch, isSidechain,
--                                  userType, requestId
-- ============================================================================

USE ClaudeGui;

-- ============================================================================
-- STEP 1: BACKUP - Crea copia completa della tabella messages
-- ============================================================================
-- Questa tabella (messages_from_jsonl) servirà per implementazione futura
-- di import batch da file .jsonl
-- ============================================================================

DROP TABLE IF EXISTS messages_from_jsonl;

CREATE TABLE messages_from_jsonl LIKE messages;

INSERT INTO messages_from_jsonl SELECT * FROM messages;

SELECT CONCAT('✅ Backup created: ', COUNT(*), ' rows copied to messages_from_jsonl') AS Status
FROM messages_from_jsonl;


-- ============================================================================
-- STEP 2: RENAME - Rinomina tabella messages → messages_from_stdout
-- ============================================================================
-- Questa tabella conterrà solo messaggi ricevuti in tempo reale da stdout
-- ============================================================================

RENAME TABLE messages TO messages_from_stdout;

SELECT '✅ Table renamed: messages → messages_from_stdout' AS Status;


-- ============================================================================
-- STEP 3: CLEANUP - Rimuovi colonne obsolete (solo da messages_from_stdout)
-- ============================================================================
-- Campi rimossi perché salvati ma mai letti/usati:
-- - parent_uuid: riferimento a messaggio parent (mai implementato)
-- - git_branch: branch git corrente (non più tracciato)
-- - is_sidechain: flag sidechain conversation (non più usato)
-- - user_type: tipo utente (non più necessario)
-- - request_id: ID richiesta API Claude (tracciato ma non usato)
-- ============================================================================

ALTER TABLE messages_from_stdout
  DROP COLUMN parent_uuid,
  DROP COLUMN git_branch,
  DROP COLUMN is_sidechain,
  DROP COLUMN user_type,
  DROP COLUMN request_id;

SELECT '✅ Obsolete columns dropped from messages_from_stdout' AS Status;


-- ============================================================================
-- STEP 4: VERIFICA - Mostra struttura finale tabelle
-- ============================================================================

SELECT '📊 FINAL TABLE STRUCTURE' AS '';

SELECT
    'messages_from_jsonl' as table_name,
    COUNT(*) as row_count,
    'BACKUP with all fields (for future batch import)' as purpose
FROM messages_from_jsonl
UNION ALL
SELECT
    'messages_from_stdout' as table_name,
    COUNT(*) as row_count,
    'CLEANED for real-time stdout messages' as purpose
FROM messages_from_stdout;

-- Mostra colonne messages_from_stdout (pulita)
DESCRIBE messages_from_stdout;

SELECT '✅ Migration completed successfully!' AS Status;
