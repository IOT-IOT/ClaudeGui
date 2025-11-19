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
            autoDownloadFontAwesome: false,
            autosave: {
                enabled: false  // Gestiamo autosave lato C#
            },
            toolbar: [
                'bold', 'italic', 'heading', '|',
                'quote', 'unordered-list', 'ordered-list', '|',
                'link', 'code', 'table', '|',
                'preview', 'side-by-side', '|',
                'undo', 'redo'
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
