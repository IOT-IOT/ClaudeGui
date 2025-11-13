namespace ClaudeGui.Blazor.Models;

/// <summary>
/// Risultato dell'import messaggi da file .jsonl con dettagli completi su successi e warning.
/// </summary>
public class MessageImportResult
{
    /// <summary>
    /// Numero di messaggi importati con successo nel database
    /// </summary>
    public int ImportedCount { get; set; }

    /// <summary>
    /// Numero di messaggi skippati a causa di campi sconosciuti
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// Numero totale di messaggi processati (importati + skippati)
    /// </summary>
    public int TotalProcessed => ImportedCount + SkippedCount;

    /// <summary>
    /// Dizionario dei campi sconosciuti per messaggio.
    /// Key = UUID del messaggio, Value = Lista di campi sconosciuti trovati in quel messaggio
    /// </summary>
    public Dictionary<string, List<string>> UnknownFieldsByMessage { get; set; } = new();

    /// <summary>
    /// Set di tutti i campi sconosciuti unici trovati nell'intero import
    /// </summary>
    public HashSet<string> AllUnknownFieldsUnique
    {
        get
        {
            var uniqueFields = new HashSet<string>();
            foreach (var fieldsList in UnknownFieldsByMessage.Values)
            {
                foreach (var field in fieldsList)
                {
                    uniqueFields.Add(field);
                }
            }
            return uniqueFields;
        }
    }

    /// <summary>
    /// Indica se sono stati trovati campi sconosciuti durante l'import
    /// </summary>
    public bool HasUnknownFields => UnknownFieldsByMessage.Count > 0;

    /// <summary>
    /// Numero di messaggi con campi sconosciuti
    /// </summary>
    public int MessagesWithUnknownFieldsCount => UnknownFieldsByMessage.Count;
}
