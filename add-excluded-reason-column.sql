-- Script per aggiungere la colonna excluded_reason alla tabella Sessions
-- Questa colonna traccia il motivo per cui una sessione è stata esclusa
-- Valori possibili: "summary", "file-history-snapshot", NULL (se non esclusa)

USE ClaudeGui;

ALTER TABLE Sessions
ADD COLUMN excluded_reason VARCHAR(100) NULL;

-- Verifica
DESCRIBE Sessions;
