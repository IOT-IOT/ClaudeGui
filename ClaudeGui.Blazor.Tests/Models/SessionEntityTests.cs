using ClaudeGui.Blazor.Models.Entities;
using FluentAssertions;
using System.ComponentModel.DataAnnotations;

namespace ClaudeGui.Blazor.Tests.Models;

/// <summary>
/// Test per l'entità Session.
/// Verifica validazione attributi e proprietà di default.
/// </summary>
public class SessionEntityTests
{
    /// <summary>
    /// Verifica che Session con dati validi passi la validazione.
    /// </summary>
    [Fact]
    public void Session_WithValidData_ShouldPassValidation()
    {
        // Arrange
        var session = new Session
        {
            SessionId = "valid-session-uuid",
            Name = "Test Session",
            WorkingDirectory = "C:\\Sources\\Test",
            Status = "open",
            LastActivity = DateTime.Now,
            CreatedAt = DateTime.Now,
            Processed = true,
            Excluded = false
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(session);
        var isValid = Validator.TryValidateObject(session, context, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue("Session con dati validi deve passare validazione");
        validationResults.Should().BeEmpty();
    }

    /// <summary>
    /// Verifica che SessionId sia Required.
    /// </summary>
    [Fact]
    public void Session_SessionId_ShouldBeRequired()
    {
        // Arrange
        var session = new Session
        {
            SessionId = null!, // Null per testare Required
            WorkingDirectory = "C:\\Test",
            Status = "open",
            LastActivity = DateTime.Now,
            CreatedAt = DateTime.Now,
            Processed = true,
            Excluded = false
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(session);
        var isValid = Validator.TryValidateObject(session, context, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse("SessionId null deve fallire validazione");
        validationResults.Should().ContainSingle(r => r.MemberNames.Contains(nameof(Session.SessionId)));
    }

    /// <summary>
    /// Verifica che SessionId rispetti StringLength(36) per UUID.
    /// </summary>
    [Fact]
    public void Session_SessionId_ShouldRespectMaxLength()
    {
        // Arrange
        var session = new Session
        {
            SessionId = new string('x', 50), // Supera 36 caratteri
            WorkingDirectory = "C:\\Test",
            Status = "open",
            LastActivity = DateTime.Now,
            CreatedAt = DateTime.Now,
            Processed = true,
            Excluded = false
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(session);
        var isValid = Validator.TryValidateObject(session, context, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse("SessionId > 36 caratteri deve fallire validazione");
        validationResults.Should().ContainSingle(r => r.MemberNames.Contains(nameof(Session.SessionId)));
    }

    /// <summary>
    /// Verifica che Name sia nullable (opzionale).
    /// </summary>
    [Fact]
    public void Session_Name_ShouldBeNullable()
    {
        // Arrange
        var session = new Session
        {
            SessionId = "test-uuid",
            Name = null, // Name è nullable
            WorkingDirectory = "C:\\Test",
            Status = "open",
            LastActivity = DateTime.Now,
            CreatedAt = DateTime.Now,
            Processed = true,
            Excluded = false
        };

        // Act
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(session);
        var isValid = Validator.TryValidateObject(session, context, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue("Name null è valido (campo opzionale)");
        validationResults.Should().BeEmpty();
    }

    /// <summary>
    /// Verifica che Processed di default sia false (impostato in InsertOrUpdateSessionAsync).
    /// Nota: L'entità non ha default value, il default viene impostato dal service layer.
    /// </summary>
    [Fact]
    public void Session_Processed_DefaultValue()
    {
        // Arrange & Act
        var session = new Session
        {
            SessionId = "test-uuid",
            WorkingDirectory = "C:\\Test",
            Status = "open",
            LastActivity = DateTime.Now,
            CreatedAt = DateTime.Now,
            Excluded = false
            // Processed non impostato esplicitamente
        };

        // Assert
        session.Processed.Should().BeFalse("default value di bool è false");
    }

    /// <summary>
    /// Verifica che Excluded di default sia false.
    /// </summary>
    [Fact]
    public void Session_Excluded_DefaultValue()
    {
        // Arrange & Act
        var session = new Session
        {
            SessionId = "test-uuid",
            WorkingDirectory = "C:\\Test",
            Status = "open",
            LastActivity = DateTime.Now,
            CreatedAt = DateTime.Now,
            Processed = true
            // Excluded non impostato esplicitamente
        };

        // Assert
        session.Excluded.Should().BeFalse("default value di bool è false");
    }
}
