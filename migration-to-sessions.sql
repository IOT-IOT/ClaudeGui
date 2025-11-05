-- Migration: Conversations → Sessions
-- Data: 2025-11-04
-- Descrizione: Ristruttura il database per supportare multi-sessione con tab

-- Step 1: Rinomina tabella conversations → Sessions
RENAME TABLE conversations TO Sessions;

-- Step 2: Aggiungi campo name per il nome assegnato dall'utente
ALTER TABLE Sessions
ADD COLUMN name VARCHAR(255) DEFAULT NULL
COMMENT 'Nome assegnato dall''utente alla sessione'
AFTER session_id;

-- Step 3: Modifica ENUM status da 4 valori a 2 (semplificato)
-- Da: ('active', 'closed', 'killed') → A: ('open', 'closed')
ALTER TABLE Sessions
MODIFY COLUMN status ENUM('open', 'closed') DEFAULT 'open'
COMMENT 'Status sessione: open (da riaprire al boot), closed (chiusa definitivamente)';

-- Step 4: Converti i valori esistenti di status
-- 'active' → 'open'
-- 'killed' → 'open' (verranno riaperte)
-- 'closed' rimane 'closed'
UPDATE Sessions SET status = 'open' WHERE status IN ('active', 'killed');

-- Step 5: Verifica risultato
SELECT COUNT(*) as total_sessions,
       SUM(CASE WHEN status = 'open' THEN 1 ELSE 0 END) as open_sessions,
       SUM(CASE WHEN status = 'closed' THEN 1 ELSE 0 END) as closed_sessions
FROM Sessions;

-- Step 6: Mostra struttura finale
SHOW CREATE TABLE Sessions;
