-- Script per inserire messaggi di test nel database
-- Usa il session_id della sessione che hai appena aperto: 99d8344b-741e-465b-935f-8d782885531e

DELETE FROM messages WHERE conversation_id = '99d8344b-741e-465b-935f-8d782885531e';

INSERT INTO messages (conversation_id, role, content, timestamp, sequence)
VALUES
    ('99d8344b-741e-465b-935f-8d782885531e', 'user', 'Ciao, come stai?', NOW(), 1),
    ('99d8344b-741e-465b-935f-8d782885531e', 'assistant', 'Ciao! Sto bene grazie, come posso aiutarti oggi?', NOW(), 2),
    ('99d8344b-741e-465b-935f-8d782885531e', 'user', 'Puoi spiegarmi come funziona questo progetto?', NOW(), 3),
    ('99d8344b-741e-465b-935f-8d782885531e', 'assistant', 'Certamente! Questo è un progetto **ClaudeCodeMAUI** che implementa una GUI multi-sessione per Claude Code.\n\nCaratteristiche principali:\n- Gestione multi-tab per sessioni multiple\n- Database MySQL per storico conversazioni\n- Rendering HTML con Markdown\n- Supporto temi chiaro/scuro', NOW(), 4),
    ('99d8344b-741e-465b-935f-8d782885531e', 'user', 'Ottimo, grazie!', NOW(), 5),
    ('99d8344b-741e-465b-935f-8d782885531e', 'assistant', 'Prego! Se hai altre domande, sono qui per aiutarti.', NOW(), 6);

SELECT * FROM messages WHERE conversation_id = '99d8344b-741e-465b-935f-8d782885531e' ORDER BY sequence;
