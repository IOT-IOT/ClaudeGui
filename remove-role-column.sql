-- Script per rimuovere la colonna 'role' e rendere 'message_type' obbligatorio
-- Database: ClaudeGui
-- Data: 2025-01-06

USE ClaudeGui;

-- Step 1: Popola message_type con i valori di role dove message_type è NULL
UPDATE messages
SET message_type = role
WHERE message_type IS NULL;

-- Step 2: Rendi message_type NOT NULL
ALTER TABLE messages
MODIFY COLUMN message_type VARCHAR(50) NOT NULL;

-- Step 3: Rimuovi la colonna role
ALTER TABLE messages
DROP COLUMN role;

-- Verifica finale
DESCRIBE messages;
