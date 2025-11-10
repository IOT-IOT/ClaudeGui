-- Script SQL per creare la tabella queue_operations nel database ClaudeGui
-- Database: MariaDB 10.11
-- Eseguire su: 192.168.1.11:3306/ClaudeGui

-- Creazione tabella queue_operations
CREATE TABLE IF NOT EXISTS `queue_operations` (
    `id` INT AUTO_INCREMENT PRIMARY KEY COMMENT 'ID autoincrementale',
    `session_id` VARCHAR(255) NOT NULL COMMENT 'ID della sessione (FK verso sessions.session_id)',
    `timestamp` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Timestamp dell\'operazione',
    `operation` VARCHAR(100) NOT NULL COMMENT 'Tipo di operazione (es. enqueue, dequeue)',
    `content` TEXT NOT NULL COMMENT 'Contenuto del messaggio accodato',

    -- Indici per performance
    INDEX `idx_queue_operations_session_id` (`session_id`),
    INDEX `idx_queue_operations_operation` (`operation`),
    INDEX `idx_queue_operations_timestamp` (`timestamp`),

    -- Foreign Key verso Sessions (case-sensitive in MariaDB)
    CONSTRAINT `fk_queue_operations_session`
        FOREIGN KEY (`session_id`)
        REFERENCES `Sessions` (`session_id`)
        ON DELETE CASCADE
        ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='Tabella per memorizzare le operazioni di accodamento messaggi di Claude';

-- Verifica creazione tabella
SELECT 'Tabella queue_operations creata con successo' AS Status;
DESCRIBE queue_operations;
