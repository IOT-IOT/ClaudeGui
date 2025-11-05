using ClaudeCodeMAUI.Views;

namespace ClaudeCodeMAUI.Extensions;

/// <summary>
/// Extension methods per Page che forniscono alternative selezionabili a DisplayAlert
/// </summary>
public static class PageExtensions
{
    /// <summary>
    /// Mostra un dialog con messaggio selezionabile (sostituisce DisplayAlert con 1 pulsante)
    /// </summary>
    /// <param name="page">Pagina corrente</param>
    /// <param name="title">Titolo del dialog</param>
    /// <param name="message">Messaggio (selezionabile e copiabile)</param>
    /// <param name="button">Etichetta del pulsante (default: "OK")</param>
    public static async Task DisplaySelectableAlert(this Page page, string title, string message, string button = "OK")
    {
        var dialog = new SelectableAlertDialog(title, message, button);
        await page.Navigation.PushModalAsync(dialog);
        await dialog.ShowAsync();
    }

    /// <summary>
    /// Mostra un dialog di conferma con messaggio selezionabile (sostituisce DisplayAlert con 2 pulsanti)
    /// </summary>
    /// <param name="page">Pagina corrente</param>
    /// <param name="title">Titolo del dialog</param>
    /// <param name="message">Messaggio (selezionabile e copiabile)</param>
    /// <param name="accept">Etichetta del pulsante di accettazione</param>
    /// <param name="cancel">Etichetta del pulsante di annullamento</param>
    /// <returns>true se l'utente ha cliccato il pulsante di accettazione, false altrimenti</returns>
    public static async Task<bool> DisplaySelectableAlert(this Page page, string title, string message, string accept, string cancel)
    {
        var dialog = new SelectableAlertDialog(title, message, accept, cancel);
        await page.Navigation.PushModalAsync(dialog);
        var result = await dialog.ShowAsync();
        return result == accept;
    }

    /// <summary>
    /// Mostra un action sheet con opzioni selezionabili (sostituisce DisplayActionSheet)
    /// </summary>
    /// <param name="page">Pagina corrente</param>
    /// <param name="title">Titolo del dialog</param>
    /// <param name="cancel">Etichetta del pulsante di annullamento</param>
    /// <param name="destruction">Etichetta del pulsante di distruzione (opzionale)</param>
    /// <param name="buttons">Altre opzioni disponibili</param>
    /// <returns>Testo del pulsante selezionato, o null se annullato</returns>
    public static async Task<string?> DisplaySelectableActionSheet(this Page page, string title, string cancel, string? destruction, params string[] buttons)
    {
        var allButtons = new List<string>();

        if (buttons != null)
            allButtons.AddRange(buttons);

        if (!string.IsNullOrEmpty(destruction))
            allButtons.Add(destruction);

        allButtons.Add(cancel);

        var dialog = new SelectableAlertDialog(title, "", allButtons.ToArray());
        await page.Navigation.PushModalAsync(dialog);
        var result = await dialog.ShowAsync();

        return result == cancel ? null : result;
    }
}
