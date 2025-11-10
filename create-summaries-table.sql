-- Script SQL per creare la tabella summaries nel database ClaudeGui
-- Database: MariaDB 10.11
-- Eseguire su: 192.168.1.11:3306/ClaudeGui

-- Creazione tabella summaries
CREATE TABLE IF NOT EXISTS `summaries` (
    `id` INT AUTO_INCREMENT PRIMARY KEY COMMENT 'ID autoincrementale',
    `session_id` VARCHAR(255) NOT NULL COMMENT 'ID della sessione (FK verso sessions.session_id)',
    `timestamp` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Timestamp di creazione del summary',
    `summary` VARCHAR(1000) NOT NULL COMMENT 'Testo del summary generato da Claude',
    `leaf_uuid` VARCHAR(255) NULL COMMENT 'UUID del messaggio finale che ha generato il summary',

    -- Indici per performance
    INDEX `idx_summaries_session_id` (`session_id`),
    INDEX `idx_summaries_leaf_uuid` (`leaf_uuid`),
    INDEX `idx_summaries_timestamp` (`timestamp`),

    -- Foreign Key verso Sessions (case-sensitive in MariaDB)
    CONSTRAINT `fk_summaries_session`
        FOREIGN KEY (`session_id`)
        REFERENCES `Sessions` (`session_id`)
        ON DELETE CASCADE
        ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='Tabella per memorizzare i summary generati da Claude durante le conversazioni';

-- Verifica creazione tabella
SELECT 'Tabella summaries creata con successo' AS Status;
DESCRIBE summaries;
