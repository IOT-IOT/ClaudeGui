using System.Collections.Generic;

namespace ClaudeCodeMAUI.Utilities
{
    /// <summary>
    /// Manages command history for arrow key navigation (↑/↓) in input TextBox.
    /// Similar to bash history functionality.
    /// </summary>
    public class CommandHistoryManager
    {
        private readonly List<string> _history;
        private int _currentIndex;
        private string _temporaryInput;
        private const int MaxHistorySize = 50;

        public CommandHistoryManager()
        {
            _history = new List<string>();
            _currentIndex = -1;
            _temporaryInput = string.Empty;
        }

        /// <summary>
        /// Adds a command to the history.
        /// Called when user presses Enter.
        /// </summary>
        public void AddCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            // Don't add duplicate of last command
            if (_history.Count > 0 && _history[_history.Count - 1] == command)
                return;

            _history.Add(command);

            // Limit history size
            if (_history.Count > MaxHistorySize)
            {
                _history.RemoveAt(0);
            }

            // Reset navigation index
            _currentIndex = -1;
            _temporaryInput = string.Empty;
        }

        /// <summary>
        /// Gets the previous command in history (↑ key).
        /// Returns null if at the beginning of history.
        /// </summary>
        public string? GetPrevious(string currentInput)
        {
            if (_history.Count == 0)
                return null;

            // First time navigating: save current input
            if (_currentIndex == -1)
            {
                _temporaryInput = currentInput;
                _currentIndex = _history.Count;
            }

            // Move backwards in history
            if (_currentIndex > 0)
            {
                _currentIndex--;
                return _history[_currentIndex];
            }

            // Already at the beginning
            return _history[_currentIndex];
        }

        /// <summary>
        /// Gets the next command in history (↓ key).
        /// Returns the temporary input when reaching the end.
        /// Returns null if not currently navigating.
        /// </summary>
        public string? GetNext()
        {
            if (_currentIndex == -1 || _history.Count == 0)
                return null;

            // Move forward in history
            _currentIndex++;

            if (_currentIndex >= _history.Count)
            {
                // Reached the end, return to current input
                _currentIndex = -1;
                var result = _temporaryInput;
                _temporaryInput = string.Empty;
                return result;
            }

            return _history[_currentIndex];
        }

        /// <summary>
        /// Resets navigation state (called when user types or modifies input)
        /// </summary>
        public void ResetNavigation()
        {
            _currentIndex = -1;
            _temporaryInput = string.Empty;
        }

        /// <summary>
        /// Clears all history
        /// </summary>
        public void Clear()
        {
            _history.Clear();
            _currentIndex = -1;
            _temporaryInput = string.Empty;
        }

        /// <summary>
        /// Gets the total number of commands in history
        /// </summary>
        public int Count => _history.Count;

        /// <summary>
        /// Checks if currently navigating through history
        /// </summary>
        public bool IsNavigating => _currentIndex != -1;
    }
}
