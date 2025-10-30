#!/usr/bin/env dotnet-script
#r "nuget: MySql.Data, 9.5.0"

using MySql.Data.MySqlClient;
using System;
using System.IO;

var host = "192.168.1.11";
var port = 3306;
var username = "empatheya";
var password = "ESokkio4534$$";

var connectionString = $"Server={host};Port={port};User={username};Password={password};SslMode=Preferred;";

Console.WriteLine("Connecting to MariaDB server...");

try
{
    using var connection = new MySqlConnection(connectionString);
    connection.Open();
    Console.WriteLine("Connected successfully!");

    // Create database
    Console.WriteLine("\nCreating database ClaudeGui...");
    using (var cmd = new MySqlCommand("CREATE DATABASE IF NOT EXISTS ClaudeGui CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci", connection))
    {
        cmd.ExecuteNonQuery();
        Console.WriteLine("Database created!");
    }

    // Switch to database
    connection.ChangeDatabase("ClaudeGui");

    // Drop existing table
    Console.WriteLine("\nDropping existing conversations table (if exists)...");
    using (var cmd = new MySqlCommand("DROP TABLE IF EXISTS conversations", connection))
    {
        cmd.ExecuteNonQuery();
        Console.WriteLine("Table dropped!");
    }

    // Create table
    Console.WriteLine("\nCreating conversations table...");
    var createTableSql = @"
CREATE TABLE conversations (
    id INT AUTO_INCREMENT PRIMARY KEY,
    session_id VARCHAR(36) NOT NULL UNIQUE COMMENT 'Claude session UUID from stream-json',
    tab_title VARCHAR(255) DEFAULT NULL COMMENT 'User-friendly tab title',
    is_plan_mode BOOLEAN DEFAULT FALSE COMMENT 'Whether plan mode was active',
    last_activity DATETIME NOT NULL COMMENT 'Last interaction timestamp',
    status ENUM('active', 'closed', 'killed') DEFAULT 'active' COMMENT 'Session status',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP COMMENT 'Session creation timestamp',
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT 'Last update',
    INDEX idx_session_id (session_id),
    INDEX idx_recovery (status, last_activity),
    INDEX idx_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";

    using (var cmd = new MySqlCommand(createTableSql, connection))
    {
        cmd.ExecuteNonQuery();
        Console.WriteLine("Table created successfully!");
    }

    // Verify
    Console.WriteLine("\nVerifying table structure...");
    using (var cmd = new MySqlCommand("DESCRIBE conversations", connection))
    using (var reader = cmd.ExecuteReader())
    {
        Console.WriteLine("\nTable structure:");
        while (reader.Read())
        {
            Console.WriteLine($"  {reader["Field"],-20} {reader["Type"],-20} {reader["Null"],-5} {reader["Key"],-5}");
        }
    }

    Console.WriteLine("\n✅ Database setup completed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error: {ex.Message}");
    Environment.Exit(1);
}
