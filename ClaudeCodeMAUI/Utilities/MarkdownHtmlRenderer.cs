using System;
using System.Text;
using System.Web;
using Markdig;
using Serilog;

namespace ClaudeCodeMAUI.Utilities
{
    /// <summary>
    /// Converte markdown in HTML e genera HTML formattato per diversi tipi di messaggi
    /// nella conversazione con Claude (user prompts, assistant responses, tool calls, tool results)
    /// </summary>
    public class MarkdownHtmlRenderer
    {
        private readonly MarkdownPipeline _pipeline;

        /// <summary>
        /// Costruttore: inizializza la pipeline Markdig con supporto GitHub-flavored markdown
        /// </summary>
        public MarkdownHtmlRenderer()
        {
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()  // GitHub-flavored markdown (tables, task lists, etc.)
                .UsePipeTables()
                .UseEmphasisExtras()
                .UseAutoLinks()
                .UseTaskLists()
                .Build();

            Log.Information("MarkdownHtmlRenderer initialized with GitHub-flavored markdown support");
        }

        /// <summary>
        /// Genera la pagina HTML completa con template, CSS e JavaScript inline
        /// </summary>
        /// <param name="isDarkTheme">Se true, usa tema scuro; altrimenti tema chiaro</param>
        /// <param name="conversationContent">Contenuto HTML della conversazione da inserire nel container (opzionale)</param>
        /// <returns>HTML completo della pagina</returns>
        public string GenerateFullPage(bool isDarkTheme = true, string conversationContent = "")
        {
            // Colori hardcoded per tema (CSS variables non supportate in .NET MAUI WebView)
            var bgColor = isDarkTheme ? "#0d1117" : "#ffffff";
            var textColor = isDarkTheme ? "#c9d1d9" : "#24292e";
            var codeBg = isDarkTheme ? "#161b22" : "#f6f8fa";
            var borderColor = isDarkTheme ? "#30363d" : "#e1e4e8";
            var userMsgBg = isDarkTheme ? "#1c2e4a" : "#f0f7ff";
            var userMsgBorder = isDarkTheme ? "#58a6ff" : "#0969da";
            var assistantMsgBg = isDarkTheme ? "#0d1117" : "#ffffff";
            var toolCallBg = isDarkTheme ? "#1f3a5f" : "#e3f2fd";
            var toolCallBorder = isDarkTheme ? "#58a6ff" : "#0969da";
            var toolSuccessBg = isDarkTheme ? "#1b3d2f" : "#e8f5e9";
            var toolSuccessBorder = isDarkTheme ? "#3fb950" : "#1a7f37";
            var toolSuccessColor = isDarkTheme ? "#3fb950" : "#1a7f37";
            var toolErrorBg = isDarkTheme ? "#3d1f1f" : "#ffebee";
            var toolErrorBorder = isDarkTheme ? "#f85149" : "#cf222e";
            var toolErrorColor = isDarkTheme ? "#f85149" : "#cf222e";
            var separatorColor = isDarkTheme ? "#21262d" : "#d0d7de";

            return $@"<!DOCTYPE html>
<html lang=""it"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Claude Conversation</title>
    <style>
        /* ===== STILI BASE (CSS HARDCODED - no CSS variables) ===== */
        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }}

        body {{
            background-color: {bgColor};
            color: {textColor};
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Helvetica', 'Arial', sans-serif;
            font-size: 14px;
            line-height: 1.6;
            padding: 12px;
            margin: 0;
        }}

        #conversation-container {{
            max-width: 100%;
        }}

        /* ===== MESSAGGI UTENTE ===== */
        .message-user {{
            background-color: {userMsgBg};
            border-left: 4px solid {userMsgBorder};
            padding: 12px 16px;
            margin: 12px 0;
            border-radius: 6px;
            font-weight: 500;
        }}

        .message-user-icon {{
            display: inline-block;
            margin-right: 6px;
            font-weight: bold;
            color: {userMsgBorder};
        }}

        /* ===== MESSAGGI ASSISTANT (MARKDOWN) ===== */
        .message-assistant {{
            background-color: {assistantMsgBg};
            padding: 8px 4px;
            margin: 10px 0;
        }}

        .message-assistant p {{
            margin: 8px 0;
        }}

        .message-assistant code {{
            background-color: {codeBg};
            border-radius: 3px;
            padding: 2px 6px;
            font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
            font-size: 0.9em;
        }}

        .message-assistant pre {{
            background-color: {codeBg};
            border: 1px solid {borderColor};
            border-radius: 6px;
            padding: 12px;
            margin: 12px 0;
            overflow-x: auto;
        }}

        .message-assistant pre code {{
            background: none;
            padding: 0;
            border-radius: 0;
        }}

        .message-assistant ul, .message-assistant ol {{
            margin: 8px 0;
            padding-left: 24px;
        }}

        .message-assistant li {{
            margin: 4px 0;
        }}

        .message-assistant table {{
            border-collapse: collapse;
            margin: 12px 0;
            width: 100%;
        }}

        .message-assistant th, .message-assistant td {{
            border: 1px solid {borderColor};
            padding: 8px;
            text-align: left;
        }}

        .message-assistant th {{
            background-color: {codeBg};
            font-weight: bold;
        }}

        /* ===== TOOL CALLS ===== */
        .tool-call {{
            background-color: {toolCallBg};
            border: 1px solid {toolCallBorder};
            border-radius: 4px;
            padding: 5px 8px;
            margin: 5px 0;
            font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
            font-size: 10px;
        }}

        .tool-call-header {{
            font-weight: bold;
            color: {toolCallBorder};
        }}

        .tool-name {{
            font-weight: bold;
        }}

        .tool-description {{
            font-weight: normal;
            opacity: 0.9;
            margin-left: 4px;
        }}

        /* ===== TOOL RESULTS ===== */
        .tool-result {{
            border-radius: 4px;
            padding: 4px 8px;
            margin: 4px 0;
            font-size: 10px;
            font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
        }}

        .tool-result-success {{
            background-color: {toolSuccessBg};
            border-left: 4px solid {toolSuccessBorder};
            color: {toolSuccessColor};
        }}

        .tool-result-error {{
            background-color: {toolErrorBg};
            border-left: 4px solid {toolErrorBorder};
            color: {toolErrorColor};
        }}

        .tool-result-badge {{
            display: inline-block;
            padding: 2px 8px;
            border-radius: 12px;
            font-size: 0.9em;
            font-weight: bold;
            margin-right: 8px;
        }}

        .badge-success {{
            background-color: {toolSuccessBorder};
            color: white;
        }}

        .badge-error {{
            background-color: {toolErrorBorder};
            color: white;
        }}

        /* ===== SEPARATORE TRA TURNI ===== */
        .conversation-separator {{
            border-top: 1px solid {separatorColor};
            margin: 20px 0;
            opacity: 0.5;
        }}

        /* ===== METADATA STATS (sotto ogni risposta di Claude) ===== */
        .metadata-container {{
            text-align: right;
            font-size: 10px;
            color: #FFD700;
            margin: 5px 0 15px 0;
            padding: 4px 8px;
            cursor: pointer;
            font-family: 'Consolas', 'Courier New', monospace;
            user-select: none;
        }}

        .metadata-container:hover {{
            opacity: 0.8;
        }}

        .metadata-content {{
            display: inline-block;
            white-space: nowrap;
        }}

        .metadata-container.collapsed .metadata-content {{
            display: none;
        }}

        .metadata-container::before {{
            content: '📊 ';
            font-size: 11px;
        }}

        .metadata-container.collapsed::before {{
            content: '📊 Stats (click to show)';
            opacity: 0.6;
        }}

        .metadata-container:not(.collapsed)::before {{
            content: '📊 ';
        }}
    </style>
</head>
<body>
    <!-- PANNELLO TEST CONTROLS -->
    <div style=""padding: 10px; background-color: #2d2d2d; margin-bottom: 10px; border: 2px solid #444;"">
        <div style=""color: #3fb950; font-weight: bold; margin-bottom: 8px;"">🧪 TEST CONTROLS</div>
        <button onclick=""testAppendMessage()"" style=""padding: 8px 16px; margin: 5px; cursor: pointer; background-color: #238636; color: white; border: none; border-radius: 4px;"">Test appendMessage (interno)</button>
        <button onclick=""testSimpleAlert()"" style=""padding: 8px 16px; margin: 5px; cursor: pointer; background-color: #1f6feb; color: white; border: none; border-radius: 4px;"">Test Alert</button>
        <button onclick=""testDomManipulation()"" style=""padding: 8px 16px; margin: 5px; cursor: pointer; background-color: #8957e5; color: white; border: none; border-radius: 4px;"">Test DOM Manipulation</button>
        <div id=""test-result"" style=""margin-top: 10px; padding: 8px; background-color: #1a1a1a; color: #3fb950; min-height: 30px; border-radius: 4px;"">Ready for testing...</div>
    </div>

    <div id=""conversation-container"">
        {conversationContent}
    </div>

    <script>
        // ===== FUNZIONI JAVASCRIPT PER INTEROPERABILITÀ CON C# =====

        // Variabile globale per passare HTML da C#
        var htmlToAppend = '';

        function appendMessage(html) {{
            const container = document.getElementById('conversation-container');
            if (!container) {{
                console.log('ERROR: container not found');
                return 'ERROR: container not found';
            }}

            // Se chiamato senza parametri, usa la variabile globale
            const actualHtml = (html !== undefined) ? html : htmlToAppend;
            console.log('appendMessage called with ' + actualHtml.length + ' chars');

            const isAtBottom = window.innerHeight + window.scrollY >= document.body.offsetHeight - 50;

            // Usa insertAdjacentHTML invece di innerHTML += per migliori performance
            container.insertAdjacentHTML('beforeend', actualHtml);
            console.log('HTML appended successfully');

            // Auto-scroll solo se l'utente era già in fondo
            if (isAtBottom) {{
                window.scrollTo({{ top: document.body.scrollHeight, behavior: 'smooth' }});
            }}

            return 'OK: appended ' + actualHtml.length + ' chars';
        }}

        // Funzione alternativa che usa la variabile globale
        function appendFromGlobal() {{
            return appendMessage(htmlToAppend);
        }}

        // ===== FUNZIONI DI TEST =====

        function testAppendMessage() {{
            console.log('testAppendMessage called');
            const testHtml = '<div style=""background-color: rgb(63,185,80); color: white; padding: 10px; margin: 10px; border-radius: 4px;"">✅ TEST MESSAGE DA PULSANTE INTERNO</div>';
            const result = appendMessage(testHtml);
            document.getElementById('test-result').innerText = 'testAppendMessage: ' + result;
            console.log('testAppendMessage result: ' + result);
        }}

        function testSimpleAlert() {{
            console.log('testSimpleAlert called');
            alert('JavaScript Alert funziona!');
            document.getElementById('test-result').innerText = 'testSimpleAlert: Alert mostrato!';
        }}

        function testDomManipulation() {{
            console.log('testDomManipulation called');
            const resultDiv = document.getElementById('test-result');
            resultDiv.style.backgroundColor = 'rgb(137,87,229)';
            resultDiv.innerText = 'testDomManipulation: DOM modificato con successo! Timestamp: ' + new Date().toLocaleTimeString();
            return 'DOM modified successfully';
        }}

        function getTestValue() {{
            console.log('getTestValue called');
            return 'JavaScript return value: ' + new Date().toISOString();
        }}

        function setTheme(theme) {{
            // Tema dinamico non supportato con CSS hardcoded
            // Richiederebbe ricaricare la pagina HTML
            alert('Theme toggle requires page reload');
        }}

        function clearConversation() {{
            document.getElementById('conversation-container').innerHTML = '';
        }}

        function scrollToBottom() {{
            window.scrollTo({{ top: document.body.scrollHeight, behavior: 'smooth' }});
        }}

        // ===== V3: Append HTML usando Base64 encoding =====
        function appendHtmlBase64(base64Html) {{
            try {{
                const container = document.getElementById('conversation-container');
                if (!container) {{
                    console.error('Container not found');
                    return 'ERROR: container not found';
                }}

                // Decode Base64 -> binary string (Latin-1)
                const binaryString = atob(base64Html);

                // Convert binary string -> Uint8Array (byte array)
                const bytes = new Uint8Array(binaryString.length);
                for (let i = 0; i < binaryString.length; i++) {{
                    bytes[i] = binaryString.charCodeAt(i);
                }}

                // Decode Uint8Array as UTF-8 -> HTML string
                const decoder = new TextDecoder('utf-8');
                const html = decoder.decode(bytes);
                console.log('Decoded HTML length: ' + html.length + ' chars (UTF-8)');

                // Appendi HTML al container
                container.insertAdjacentHTML('beforeend', html);
                console.log('HTML appended successfully');

                // Auto-scroll in fondo
                window.scrollTo({{ top: document.body.scrollHeight, behavior: 'instant' }});

                return 'OK: appended ' + html.length + ' chars';
            }} catch (error) {{
                console.error('Error in appendHtmlBase64:', error);
                return 'ERROR: ' + error.message;
            }}
        }}
    </script>

    <!-- Auto-scroll alla fine della pagina quando viene caricata -->
    <script>
        window.scrollTo({{ top: document.body.scrollHeight, behavior: 'instant' }});
    </script>
</body>
</html>";
        }

        /// <summary>
        /// Converte markdown in HTML usando Markdig
        /// </summary>
        /// <param name="markdown">Testo markdown da convertire</param>
        /// <returns>HTML formattato</returns>
        public string RenderMarkdown(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return string.Empty;

            try
            {
                return Markdown.ToHtml(markdown, _pipeline);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to render markdown: {Markdown}", markdown.Length > 100 ? markdown.Substring(0, 100) + "..." : markdown);
                return HttpUtility.HtmlEncode(markdown);  // Fallback a plain text
            }
        }

        /// <summary>
        /// Genera HTML per un messaggio dell'utente (prompt)
        /// </summary>
        /// <param name="text">Testo del prompt utente</param>
        /// <returns>HTML del messaggio utente</returns>
        public string RenderUserMessage(string text)
        {
            var escapedText = HttpUtility.HtmlEncode(text);
            // Usa stili inline invece di classi CSS
            return $@"<div style=""background-color: #1c2e4a; border-left: 4px solid #58a6ff; padding: 12px 16px; margin: 12px 0; border-radius: 6px; font-weight: 500; color: #c9d1d9;"">
    <span style=""display: inline-block; margin-right: 6px; font-weight: bold; color: #58a6ff;"">▶</span>
    <span>{escapedText}</span>
</div>";
        }

        /// <summary>
        /// Genera HTML per un messaggio dell'assistant (risposta di Claude)
        /// Il testo viene processato come markdown usando Markdig
        /// </summary>
        /// <param name="markdownText">Testo markdown della risposta</param>
        /// <returns>HTML del messaggio assistant con markdown renderizzato</returns>
        public string RenderAssistantMessage(string markdownText)
        {
            // Renderizza il markdown in HTML usando Markdig
            var html = RenderMarkdown(markdownText);

            // Wrappa il contenuto markdown renderizzato in un div con stili
            return $@"<div class=""message-assistant"">
    {html}
</div>";
        }

        /// <summary>
        /// Genera HTML per una chiamata a un tool
        /// </summary>
        /// <param name="toolName">Nome del tool (es. "Read", "Write", "Bash")</param>
        /// <param name="description">Descrizione della chiamata (opzionale)</param>
        /// <returns>HTML della tool call</returns>
        public string RenderToolCall(string toolName, string description = "")
        {
            var escapedToolName = HttpUtility.HtmlEncode(toolName);
            var escapedDescription = HttpUtility.HtmlEncode(description);

            var descriptionHtml = string.IsNullOrWhiteSpace(escapedDescription)
                ? ""
                : $@"<span class=""tool-description"">: {escapedDescription}</span>";

            return $@"<div class=""tool-call"">
    <div class=""tool-call-header"">
        <span class=""tool-call-icon"">🔧</span>
        <span class=""tool-name"">{escapedToolName}</span>{descriptionHtml}
    </div>
</div>";
        }

        /// <summary>
        /// Genera HTML per il risultato di un tool
        /// </summary>
        /// <param name="content">Contenuto del risultato</param>
        /// <param name="isError">True se è un errore, false se è successo</param>
        /// <returns>HTML del tool result</returns>
        public string RenderToolResult(string content, bool isError)
        {
            var statusClass = isError ? "tool-result-error" : "tool-result-success";
            var badgeClass = isError ? "badge-error" : "badge-success";
            var badgeText = isError ? "✗ ERROR" : "✓ OK";

            var escapedContent = HttpUtility.HtmlEncode(content);

            // Se il contenuto è molto lungo, lo tronchiamo
            if (escapedContent.Length > 200)
            {
                escapedContent = escapedContent.Substring(0, 200) + "...";
            }

            return $@"<div class=""tool-result {statusClass}"">
    <span class=""tool-result-badge {badgeClass}"">{badgeText}</span>
    <span class=""tool-result-content"">{escapedContent}</span>
</div>";
        }

        /// <summary>
        /// Genera un separatore visivo tra turni di conversazione
        /// </summary>
        /// <returns>HTML del separatore</returns>
        public string RenderSeparator()
        {
            return @"<div class=""conversation-separator""></div>";
        }

        /// <summary>
        /// Genera HTML per la visualizzazione dei metadata di una conversazione
        /// (questo era precedentemente nella metadata bar, ora potrebbe essere inline)
        /// </summary>
        /// <param name="durationMs">Durata in millisecondi</param>
        /// <param name="costUsd">Costo in USD</param>
        /// <param name="inputTokens">Token di input</param>
        /// <param name="outputTokens">Token di output</param>
        /// <param name="numTurns">Numero di turni</param>
        /// <returns>HTML dei metadata (non usato attualmente, ma disponibile per futuri usi)</returns>
        public string RenderMetadata(long durationMs, decimal costUsd, int inputTokens, int outputTokens, int numTurns)
        {
            return $@"<div class=""metadata"">
    Duration: {durationMs}ms | Cost: ${costUsd:F4} | Tokens: {inputTokens} in / {outputTokens} out | Turns: {numTurns}
</div>";
        }
    }
}
