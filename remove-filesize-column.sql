-- Script per rimuovere la colonna file_size_bytes dalla tabella Sessions
-- La dimensione del file è un dato dinamico che verrà calcolato al volo durante la scansione
USE ClaudeGui;

ALTER TABLE Sessions
DROP COLUMN file_size_bytes;

-- Verifica
DESCRIBE Sessions;
