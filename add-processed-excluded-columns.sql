-- Script per aggiungere colonne processed ed excluded alla tabella Sessions
-- Eseguire su database ClaudeGui

USE ClaudeGui;

-- Aggiungi colonne per tracciare lo stato di processamento ed esclusione
ALTER TABLE Sessions
ADD COLUMN processed BOOLEAN DEFAULT FALSE,
ADD COLUMN excluded BOOLEAN DEFAULT FALSE;

-- Crea indice per ottimizzare le query di filtraggio
CREATE INDEX idx_sessions_processed_excluded ON Sessions(processed, excluded);

-- Verifica le modifiche
DESCRIBE Sessions;

-- Mostra statistiche
SELECT
    COUNT(*) as total_sessions,
    SUM(CASE WHEN processed = TRUE THEN 1 ELSE 0 END) as processed_count,
    SUM(CASE WHEN excluded = TRUE THEN 1 ELSE 0 END) as excluded_count
FROM Sessions;
