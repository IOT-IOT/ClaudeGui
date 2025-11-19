using ClaudeGui.Blazor.Models.Entities;
using FluentAssertions;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ClaudeGui.Blazor.Tests.Models;

/// <summary>
/// Test per l'entità Message.
/// Verifica deserializzazione JSON, validazione attributi, e mapping proprietà.
/// </summary>
public class MessageEntityTests
{
    /// <summary>
    /// Verifica che Message possa essere deserializzato da JSON con tutte le proprietà.
    /// </summary>
    [Fact]
    public void Message_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = """
        {
            "id": 123,
            "conversation_id": "test-session-id",
            "content": "Hello Claude!",
            "timestamp": "2025-11-12T14:30:00",
            "uuid": "msg-uuid-12345",
            "version": "0.9.4",
            "cwd": "C:/Test",
            "model": "claude-sonnet-4-5-20250929",
            "usage_json": "{}",
            "message_type": "user"
        }
        """;

        // Act
        var message = JsonSerializer.Deserialize<Message>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        // Assert
        message.Should().NotBeNull();
        message!.Id.Should().Be(123);
        message.ConversationId.Should().Be("test-session-id");
        message.Content.Should().Be("Hello Claude!");
        message.Uuid.Should().Be("msg-uuid-12345");
        message.Version.Should().Be("0.9.4");
        message.Cwd.Should().Be("C:/Test");
        message.Model.Should().Be("claude-sonnet-4-5-20250929");
        message.MessageType.Should().Be("user");
    }

    /// <summary>
    /// Verifica che l'attributo [Required] su ConversationId sia rispettato.
    /// L'attributo [Required] rifiuta sia null che empty string per default.
    /// </summary>
    [Fact]
    public void Message_ConversationId_ShouldBeRequired()
    {
        // Arrange - Test con empty string
        var message = new Message
        {
            ConversationId = "", // Empty string
            Content = "Test content",
            Timestamp = DateTime.Now,
            Uuid = "test-uuid",
            MessageType = "user"
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(message);
        var isValid = Validator.TryValidateObject(message, context, validationResults, validateAllProperties: true);

        // Assert - empty string deve fallire validazione
        isValid.Should().BeFalse("ConversationId empty string deve fallire validazione");
        validationResults.Should().ContainSingle(r => r.MemberNames.Contains(nameof(Message.ConversationId)));

        // Test con null
        message.ConversationId = null!;
        validationResults.Clear();
        isValid = Validator.TryValidateObject(message, context, validationResults, validateAllProperties: true);
        isValid.Should().BeFalse("ConversationId null deve fallire validazione");
        validationResults.Should().ContainSingle(r => r.MemberNames.Contains(nameof(Message.ConversationId)));
    }

    /// <summary>
    /// Verifica che l'attributo [Required] su Content sia rispettato.
    /// </summary>
    [Fact]
    public void Message_Content_ShouldBeRequired()
    {
        // Arrange
        var message = new Message
        {
            ConversationId = "test-session",
            Content = null!, // Null per testare Required
            Timestamp = DateTime.Now,
            Uuid = "test-uuid",
            MessageType = "user"
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(message);
        var isValid = Validator.TryValidateObject(message, context, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse("Content null deve fallire validazione");
        validationResults.Should().ContainSingle(r => r.MemberNames.Contains(nameof(Message.Content)));
    }

    /// <summary>
    /// Verifica che l'attributo [StringLength] su Version sia rispettato.
    /// </summary>
    [Fact]
    public void Message_Version_ShouldRespectMaxLength()
    {
        // Arrange
        var message = new Message
        {
            ConversationId = "test-session",
            Content = "Test content",
            Timestamp = DateTime.Now,
            Uuid = "test-uuid",
            MessageType = "user",
            Version = new string('x', 25) // Supera StringLength(20)
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(message);
        var isValid = Validator.TryValidateObject(message, context, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse("Version > 20 caratteri deve fallire validazione");
        validationResults.Should().ContainSingle(r => r.MemberNames.Contains(nameof(Message.Version)));
    }

    /// <summary>
    /// Verifica che Message possa essere creato con valori validi.
    /// </summary>
    [Fact]
    public void Message_WithValidData_ShouldPassValidation()
    {
        // Arrange
        var message = new Message
        {
            ConversationId = "valid-session-id",
            Content = "Valid message content",
            Timestamp = DateTime.Now,
            Uuid = "valid-uuid-12345",
            Version = "0.9.4",
            Cwd = "C:\\Valid\\Path",
            Model = "claude-sonnet-4-5-20250929",
            UsageJson = "{}",
            MessageType = "assistant"
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(message);
        var isValid = Validator.TryValidateObject(message, context, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue("Message con dati validi deve passare validazione");
        validationResults.Should().BeEmpty();
    }

    /// <summary>
    /// Verifica che la navigation property Session sia nullable.
    /// </summary>
    [Fact]
    public void Message_SessionNavigationProperty_ShouldBeNullable()
    {
        // Arrange & Act
        var message = new Message
        {
            ConversationId = "test-session",
            Content = "Test",
            Timestamp = DateTime.Now,
            Uuid = "test-uuid",
            MessageType = "user",
            Session = null // Navigation property può essere null
        };

        // Assert
        message.Session.Should().BeNull("navigation property può essere null prima del caricamento EF Core");
    }
}
