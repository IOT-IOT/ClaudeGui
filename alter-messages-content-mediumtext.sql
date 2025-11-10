-- Script SQL per espandere la colonna content nella tabella messages
-- Database: MariaDB 10.11
-- Eseguire su: 192.168.1.11:3306/ClaudeGui
-- Stato: ESEGUITO (verificare con DESCRIBE messages)

-- Modifica colonna content da TEXT (65KB) a MEDIUMTEXT (16MB)
-- Necessario per supportare messaggi molto lunghi di Claude (risposte con codice esteso, documentazione, ecc.)
ALTER TABLE `messages`
MODIFY COLUMN `content` MEDIUMTEXT NOT NULL
COMMENT 'Contenuto del messaggio (può includere markdown, codice, risposte lunghe)';

-- Verifica modifica
SELECT 'Colonna content modificata a MEDIUMTEXT' AS Status;
DESCRIBE messages;
