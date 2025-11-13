namespace ClaudeGui.Blazor.Services;

/// <summary>
/// Servizio per gestire eventi di sessione tra componenti.
/// Permette comunicazione tra Index.razor e NavMenu.razor.
/// </summary>
public class SessionEventService
{
    /// <summary>
    /// Evento invocato quando una sessione viene chiusa e NavMenu deve essere aggiornato.
    /// </summary>
    public event EventHandler? SessionListChanged;

    /// <summary>
    /// Notifica che la lista sessioni è cambiata e deve essere ricaricata.
    /// </summary>
    public void NotifySessionListChanged()
    {
        SessionListChanged?.Invoke(this, EventArgs.Empty);
    }
}
