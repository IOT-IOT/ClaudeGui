using System;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeCodeMAUI.Utilities
{
    /// <summary>
    /// Converte ANSI escape codes in HTML con colori e stili.
    /// Claude Code usa ANSI codes per colorare il testo nei terminali.
    /// </summary>
    public static class AnsiToHtmlConverter
    {
        // Regex per trovare ANSI escape sequences: ESC[...m
        private static readonly Regex AnsiRegex = new Regex(@"\x1B\[([0-9;]+)m", RegexOptions.Compiled);

        // Mappatura colori ANSI standard a codici HTML
        private static readonly Dictionary<int, string> AnsiColors = new Dictionary<int, string>
        {
            // Colori base (30-37: foreground, 40-47: background)
            { 30, "#000000" }, // Black
            { 31, "#CD3131" }, // Red
            { 32, "#0DBC79" }, // Green
            { 33, "#E5E510" }, // Yellow
            { 34, "#2472C8" }, // Blue
            { 35, "#BC3FBC" }, // Magenta
            { 36, "#11A8CD" }, // Cyan
            { 37, "#E5E5E5" }, // White

            // Colori bright (90-97: bright foreground, 100-107: bright background)
            { 90, "#666666" }, // Bright Black (Gray)
            { 91, "#F14C4C" }, // Bright Red
            { 92, "#23D18B" }, // Bright Green
            { 93, "#F5F543" }, // Bright Yellow
            { 94, "#3B8EEA" }, // Bright Blue
            { 95, "#D670D6" }, // Bright Magenta
            { 96, "#29B8DB" }, // Bright Cyan
            { 97, "#FFFFFF" }, // Bright White
        };

        /// <summary>
        /// Converte testo con ANSI escape codes in HTML con span colorati.
        /// </summary>
        /// <param name="ansiText">Testo con ANSI codes</param>
        /// <returns>HTML con colori</returns>
        public static string Convert(string ansiText)
        {
            if (string.IsNullOrEmpty(ansiText))
                return ansiText;

            // Se non ci sono ANSI codes, ritorna il testo originale
            if (!ansiText.Contains("\x1B["))
                return ansiText;

            var result = new StringBuilder();
            var currentForeground = "";
            var currentBold = false;
            var spanOpen = false;

            int lastIndex = 0;
            var matches = AnsiRegex.Matches(ansiText);

            foreach (Match match in matches)
            {
                // Aggiungi il testo prima dell'escape code
                if (match.Index > lastIndex)
                {
                    var textBefore = ansiText.Substring(lastIndex, match.Index - lastIndex);
                    result.Append(System.Web.HttpUtility.HtmlEncode(textBefore));
                }

                // Chiudi lo span precedente se aperto
                if (spanOpen)
                {
                    result.Append("</span>");
                    spanOpen = false;
                }

                // Parsa i codici ANSI
                var codes = match.Groups[1].Value.Split(';');
                foreach (var codeStr in codes)
                {
                    if (int.TryParse(codeStr, out int code))
                    {
                        if (code == 0)
                        {
                            // Reset: torna a stile normale
                            currentForeground = "";
                            currentBold = false;
                        }
                        else if (code == 1)
                        {
                            // Bold
                            currentBold = true;
                        }
                        else if (code == 22)
                        {
                            // Not bold
                            currentBold = false;
                        }
                        else if ((code >= 30 && code <= 37) || (code >= 90 && code <= 97))
                        {
                            // Foreground color
                            if (AnsiColors.TryGetValue(code, out var color))
                            {
                                currentForeground = color;
                            }
                        }
                        else if (code == 39)
                        {
                            // Default foreground
                            currentForeground = "";
                        }
                    }
                }

                // Apri nuovo span con gli stili correnti
                if (!string.IsNullOrEmpty(currentForeground) || currentBold)
                {
                    result.Append("<span style=\"");
                    if (!string.IsNullOrEmpty(currentForeground))
                    {
                        result.Append($"color:{currentForeground};");
                    }
                    if (currentBold)
                    {
                        result.Append("font-weight:bold;");
                    }
                    result.Append("\">");
                    spanOpen = true;
                }

                lastIndex = match.Index + match.Length;
            }

            // Aggiungi il testo rimanente
            if (lastIndex < ansiText.Length)
            {
                var remaining = ansiText.Substring(lastIndex);
                result.Append(System.Web.HttpUtility.HtmlEncode(remaining));
            }

            // Chiudi l'ultimo span se aperto
            if (spanOpen)
            {
                result.Append("</span>");
            }

            return result.ToString();
        }

        /// <summary>
        /// Rimuove tutti i codici ANSI da un testo, lasciando solo il testo pulito.
        /// </summary>
        /// <param name="ansiText">Testo con ANSI codes</param>
        /// <returns>Testo senza ANSI codes</returns>
        public static string StripAnsiCodes(string ansiText)
        {
            if (string.IsNullOrEmpty(ansiText))
                return ansiText;

            return AnsiRegex.Replace(ansiText, "");
        }

        /// <summary>
        /// Verifica se un testo contiene codici ANSI.
        /// </summary>
        /// <param name="text">Testo da controllare</param>
        /// <returns>True se contiene ANSI codes</returns>
        public static bool ContainsAnsiCodes(string text)
        {
            return !string.IsNullOrEmpty(text) && text.Contains("\x1B[");
        }
    }
}
