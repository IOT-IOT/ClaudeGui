-- ClaudeCodeGUI Database Schema
-- MariaDB / MySQL
--
-- Instructions:
-- 1. Create the database: CREATE DATABASE claudecodegui CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
-- 2. Run this script: mysql -u username -p claudecodegui < database-schema.sql

-- Drop table if exists (for clean setup)
DROP TABLE IF EXISTS conversations;

-- Create conversations table
CREATE TABLE conversations (
    id INT AUTO_INCREMENT PRIMARY KEY,
    session_id VARCHAR(36) NOT NULL UNIQUE COMMENT 'Claude session UUID from stream-json',
    tab_title VARCHAR(255) DEFAULT NULL COMMENT 'User-friendly tab title (first 30 chars of prompt)',
    is_plan_mode BOOLEAN DEFAULT FALSE COMMENT 'Whether plan mode was active for this session',
    last_activity DATETIME NOT NULL COMMENT 'Last interaction timestamp',
    status ENUM('active', 'closed', 'killed') DEFAULT 'active' COMMENT 'Session status: active (running), closed (normally terminated), killed (force stopped)',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP COMMENT 'Session creation timestamp',
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT 'Last update timestamp',

    -- Indexes for performance
    INDEX idx_session_id (session_id),
    INDEX idx_recovery (status, last_activity) COMMENT 'Index for recovery queries (WHERE status IN (active, killed))',
    INDEX idx_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Stores Claude Code conversation sessions for persistence and recovery';

-- Example queries:

-- Recovery query (used at app startup)
-- SELECT * FROM conversations WHERE status IN ('active', 'killed') ORDER BY last_activity DESC;

-- Insert new session
-- INSERT INTO conversations (session_id, tab_title, is_plan_mode, last_activity, status)
-- VALUES ('abc123-...', 'Implement user auth', FALSE, NOW(), 'active');

-- Update status on kill
-- UPDATE conversations SET status = 'killed', updated_at = NOW() WHERE session_id = 'abc123-...';

-- Update status on normal close
-- UPDATE conversations SET status = 'closed', updated_at = NOW() WHERE session_id = 'abc123-...';

-- Update last activity (periodic)
-- UPDATE conversations SET last_activity = NOW(), updated_at = NOW() WHERE session_id = 'abc123-...';

-- Batch update on app close
-- UPDATE conversations SET status = 'closed', updated_at = NOW() WHERE session_id IN ('abc123-...', 'def456-...', ...);

-- Cleanup old closed conversations (optional maintenance)
-- DELETE FROM conversations WHERE status = 'closed' AND last_activity < DATE_SUB(NOW(), INTERVAL 30 DAY);
