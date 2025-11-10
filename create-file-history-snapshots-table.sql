-- Script SQL per creare la tabella file_history_snapshots nel database ClaudeGui
-- Database: MariaDB 10.11
-- Eseguire su: 192.168.1.11:3306/ClaudeGui

-- Creazione tabella file_history_snapshots
CREATE TABLE IF NOT EXISTS `file_history_snapshots` (
    `id` INT AUTO_INCREMENT PRIMARY KEY COMMENT 'ID autoincrementale',
    `session_id` VARCHAR(255) NOT NULL COMMENT 'ID della sessione (FK verso sessions.session_id)',
    `timestamp` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Timestamp dello snapshot',
    `message_id` VARCHAR(255) NOT NULL COMMENT 'Message ID univoco dello snapshot',
    `tracked_file_backups_json` TEXT NOT NULL COMMENT 'JSON con i backup dei file tracciati',
    `is_snapshot_update` BOOLEAN NOT NULL DEFAULT FALSE COMMENT 'Flag aggiornamento snapshot esistente',

    -- Indici per performance
    INDEX `idx_file_history_snapshots_session_id` (`session_id`),
    INDEX `idx_file_history_snapshots_message_id` (`message_id`),
    INDEX `idx_file_history_snapshots_timestamp` (`timestamp`),

    -- Foreign Key verso Sessions (case-sensitive in MariaDB)
    CONSTRAINT `fk_file_history_snapshots_session`
        FOREIGN KEY (`session_id`)
        REFERENCES `Sessions` (`session_id`)
        ON DELETE CASCADE
        ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='Tabella per memorizzare gli snapshot della cronologia file tracciati da Claude';

-- Verifica creazione tabella
SELECT 'Tabella file_history_snapshots creata con successo' AS Status;
DESCRIBE file_history_snapshots;
