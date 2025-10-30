using MySql.Data.MySqlClient;
using System;

var host = "192.168.1.11";
var port = 3306;
var username = "empatheya";
var password = "ESokkio4534$$";

var connectionString = $"Server={host};Port={port};User={username};Password={password};SslMode=Preferred;ConnectionTimeout=10;";

Console.WriteLine("===========================================");
Console.WriteLine("ClaudeCodeGUI - Database Setup");
Console.WriteLine("===========================================\n");
Console.WriteLine($"Connecting to MariaDB server at {host}:{port}...");

try
{
    using var connection = new MySqlConnection(connectionString);
    connection.Open();
    Console.WriteLine("✓ Connected successfully!\n");

    // Create database
    Console.WriteLine("Creating database 'ClaudeGui'...");
    using (var cmd = new MySqlCommand("CREATE DATABASE IF NOT EXISTS ClaudeGui CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci", connection))
    {
        cmd.ExecuteNonQuery();
        Console.WriteLine("✓ Database created!\n");
    }

    // Switch to database
    connection.ChangeDatabase("ClaudeGui");
    Console.WriteLine("Switched to database 'ClaudeGui'\n");

    // Drop existing table
    Console.WriteLine("Dropping existing 'conversations' table (if exists)...");
    using (var cmd = new MySqlCommand("DROP TABLE IF EXISTS conversations", connection))
    {
        cmd.ExecuteNonQuery();
        Console.WriteLine("✓ Table dropped!\n");
    }

    // Create table
    Console.WriteLine("Creating 'conversations' table...");
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Stores Claude Code conversation sessions'";

    using (var cmd = new MySqlCommand(createTableSql, connection))
    {
        cmd.ExecuteNonQuery();
        Console.WriteLine("✓ Table created successfully!\n");
    }

    // Verify
    Console.WriteLine("Verifying table structure...");
    using (var cmd = new MySqlCommand("DESCRIBE conversations", connection))
    using (var reader = cmd.ExecuteReader())
    {
        Console.WriteLine("\nTable 'conversations' structure:");
        Console.WriteLine("─────────────────────────────────────────────────────────────");
        Console.WriteLine($"{"Field",-20} {"Type",-25} {"Null",-5} {"Key",-5}");
        Console.WriteLine("─────────────────────────────────────────────────────────────");

        while (reader.Read())
        {
            var field = reader["Field"]?.ToString() ?? "";
            var type = reader["Type"]?.ToString() ?? "";
            var nullValue = reader["Null"]?.ToString() ?? "";
            var key = reader["Key"]?.ToString() ?? "";

            Console.WriteLine($"{field,-20} {type,-25} {nullValue,-5} {key,-5}");
        }
        Console.WriteLine("─────────────────────────────────────────────────────────────");
    }

    Console.WriteLine("\n✅ Database setup completed successfully!");
    Console.WriteLine("\nYou can now run ClaudeCodeGUI application.");
    Console.WriteLine("===========================================\n");
}
catch (MySqlException ex)
{
    Console.WriteLine($"\n❌ MySQL Error: {ex.Message}");
    Console.WriteLine($"Error Code: {ex.Number}");
    Console.WriteLine($"\nPlease check:");
    Console.WriteLine("- MariaDB server is running at {0}:{1}", host, port);
    Console.WriteLine("- Network connectivity is available");
    Console.WriteLine("- Username and password are correct");
    Environment.Exit(1);
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error: {ex.Message}");
    Environment.Exit(1);
}
