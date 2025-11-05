-- Script veloce per creare la tabella Sessions nel database ClaudeGui
-- Eseguire con: mysql -h 192.168.1.11 -u root -p ClaudeGui < create-sessions-table-quick.sql

USE ClaudeGui;

-- Drop table se esiste (per test)
-- DROP TABLE IF EXISTS Sessions;

-- Crea tabella Sessions
CREATE TABLE IF NOT EXISTS Sessions (
    id INT AUTO_INCREMENT PRIMARY KEY,
    session_id VARCHAR(36) NOT NULL UNIQUE COMMENT 'UUID sessione Claude',
    name VARCHAR(255) DEFAULT NULL COMMENT 'Nome assegnato dall''utente',
    working_directory VARCHAR(500) NOT NULL COMMENT 'Working directory della sessione',
    last_activity DATETIME NOT NULL COMMENT 'Ultima attività registrata',
    status ENUM('open', 'closed') DEFAULT 'open' COMMENT 'Status: open (riapri al boot), closed (chiusa)',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP COMMENT 'Data creazione record',
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT 'Data ultimo aggiornamento',

    INDEX idx_session_id (session_id),
    INDEX idx_status (status),
    INDEX idx_working_directory (working_directory(255))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='Tabella sessioni Claude Code (multi-sessione)';

-- Verifica creazione
SELECT 'Tabella Sessions creata con successo!' AS Result;
DESCRIBE Sessions;
