-- ClaudeCodeGUI Database Schema - Sessions Table
-- MariaDB / MySQL
-- Aggiornato: 2025-11-04
-- Versione: 2.0 (Multi-sessione con Tab)

-- Drop table if exists (for clean setup)
DROP TABLE IF EXISTS Sessions;
DROP TABLE IF EXISTS conversations; -- Elimina vecchia tabella se esiste

-- Create Sessions table
CREATE TABLE Sessions (
    id INT AUTO_INCREMENT PRIMARY KEY,
    session_id VARCHAR(36) NOT NULL UNIQUE COMMENT 'Claude session UUID from stream-json',
    name VARCHAR(255) DEFAULT NULL COMMENT 'Nome assegnato dall''utente alla sessione',
    working_directory VARCHAR(500) NOT NULL COMMENT 'Working directory del progetto Claude',
    last_activity DATETIME NOT NULL COMMENT 'Last interaction timestamp',
    status ENUM('open', 'closed') DEFAULT 'open' COMMENT 'Session status: open (riapri al boot), closed (chiusa definitivamente)',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP COMMENT 'Session creation timestamp',
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT 'Last update timestamp',

    -- Indexes for performance
    INDEX idx_session_id (session_id),
    INDEX idx_status (status),
    INDEX idx_working_directory (working_directory(255))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='Stores Claude Code conversation sessions for multi-tab interface';

-- Example queries:

-- Recovery query (used at app startup)
-- SELECT * FROM Sessions WHERE status = 'open' ORDER BY last_activity DESC;

-- Insert new session
-- INSERT INTO Sessions (session_id, name, working_directory, last_activity, status)
-- VALUES ('abc123-...', 'My Project Auth', 'C:\Sources\MyProject', NOW(), 'open');

-- Update session name
-- UPDATE Sessions SET name = 'New Name', updated_at = NOW() WHERE session_id = 'abc123-...';

-- Update status on close
-- UPDATE Sessions SET status = 'closed', updated_at = NOW() WHERE session_id = 'abc123-...';

-- Update last activity (periodic)
-- UPDATE Sessions SET last_activity = NOW(), updated_at = NOW() WHERE session_id = 'abc123-...';

-- Get session by ID
-- SELECT * FROM Sessions WHERE session_id = 'abc123-...';

-- Cleanup old closed sessions (optional maintenance)
-- DELETE FROM Sessions WHERE status = 'closed' AND last_activity < DATE_SUB(NOW(), INTERVAL 30 DAY);
