-- ====================================================================
-- ClaudeCodeGUI - Database Setup Script
-- ====================================================================
--
-- Server: 192.168.1.11:3306
-- Database: ClaudeGui
-- User: empatheya
--
-- Execute this script with your MariaDB client of choice:
-- - phpMyAdmin
-- - HeidiSQL
-- - MySQL Workbench
-- - Command line: mysql -h 192.168.1.11 -u empatheya -p < setup-database.sql
--
-- ====================================================================

-- Create database if it doesn't exist
CREATE DATABASE IF NOT EXISTS ClaudeGui
CHARACTER SET utf8mb4
COLLATE utf8mb4_unicode_ci;

-- Use the database
USE ClaudeGui;

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

-- Verify table creation
SELECT 'Database ClaudeGui and table conversations created successfully!' AS Status;

-- Show table structure
DESCRIBE conversations;
