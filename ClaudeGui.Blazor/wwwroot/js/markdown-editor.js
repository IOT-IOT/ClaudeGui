/// <summary>
/// JavaScript interop per EasyMDE Markdown Editor
/// </summary>
window.MarkdownEditor = {
    editors: new Map(),

    initialize: function(elementId, dotNetRef, initialContent) {
        const element = document.getElementById(elementId);
        if (!element) {
            console.error('[MarkdownEditor] Element not found:', elementId);
            return;
        }

        const editor = new EasyMDE({
            element: element,
            initialValue: initialContent || "",
            spellChecker: false,
            autoDownloadFontAwesome: true,  // Abilita Font Awesome per icone toolbar
            autosave: {
                enabled: false  // Gestiamo autosave lato C#
            },
            toolbar: [
                // Riga 1: Formattazione + Liste + Inserimenti
                'bold', 'italic', 'heading', 'strikethrough', '|',
                'quote', 'unordered-list', 'ordered-list', '|',
                'link', 'image', 'table', 'code', 'horizontal-rule',
                // Separator forzato per andare a capo (EasyMDE supporta "\n" ma usiamo '|' come divisore visivo)
                '|',
                // Riga 2: Visualizzazione + Azioni + Aiuto
                'preview', 'side-by-side', 'fullscreen', '|',
                'undo', 'redo', '|',
                'guide'
            ],
            renderingConfig: {
                singleLineBreaks: false,
                codeSyntaxHighlighting: true
            },
            status: ['lines', 'words', 'cursor']
        });

        // Notifica C# quando contenuto cambia
        let changeTimeout;
        editor.codemirror.on('change', () => {
            clearTimeout(changeTimeout);
            changeTimeout = setTimeout(() => {
                const content = editor.value();
                dotNetRef.invokeMethodAsync('OnContentChanged', content);
            }, 300);  // Debounce 300ms per ridurre chiamate
        });

        this.editors.set(elementId, { editor, dotNetRef });
        console.log('[MarkdownEditor] Initialized:', elementId);
    },

    setContent: function(elementId, content) {
        const editorData = this.editors.get(elementId);
        if (editorData) {
            editorData.editor.value(content);
        }
    },

    getContent: function(elementId) {
        const editorData = this.editors.get(elementId);
        return editorData ? editorData.editor.value() : "";
    },

    dispose: function(elementId) {
        const editorData = this.editors.get(elementId);
        if (editorData) {
            editorData.editor.toTextArea();
            editorData.editor = null;
            this.editors.delete(elementId);
            console.log('[MarkdownEditor] Disposed:', elementId);
        }
    }
};
