namespace SSMPServer.Command;

/// <summary>
/// Input manager for console command-line input.
/// </summary>
internal class ConsoleInputManager {
    /// <summary>
    /// Event that is called when input is given by the user.
    /// </summary>
    public event Action<string>? ConsoleInputEvent;

    /// <summary>
    /// Object for locking asynchronous access.
    /// </summary>
    private readonly Lock _lock = new();

    /// <summary>
    /// The currently inputted text in the console.
    /// </summary>
    private string _currentInput;

    /// <summary>
    /// The cancellation token source for the task of reading input.
    /// </summary>
    private CancellationTokenSource? _readingTaskTokenSource;

    /// <inheritdoc cref="_currentInput" />
    private string CurrentInput {
        get {
            lock (_lock) {
                return _currentInput;
            }
        }
        set {
            lock (_lock) {
                _currentInput = value;
            }
        }
    }

    /// <summary>
    /// Construct the console input manager by initializing values.
    /// </summary>
    public ConsoleInputManager() {
        _currentInput = "";
    }

    /// <summary>
    /// Starts the console input manager.
    /// </summary>
    public void Start() {
        // Start a thread with cancellation token to read user input
        _readingTaskTokenSource = new CancellationTokenSource();
        new Thread(() => StartReading(_readingTaskTokenSource.Token)).Start();
    }

    /// <summary>
    /// Stops the console input manager.
    /// </summary>
    public void Stop() {
        _readingTaskTokenSource?.Cancel();
    }

    /// <summary>
    /// Starts the read loop for command-line input.
    /// </summary>
    /// <param name="token">The cancellation token for checking whether this task is requested to cancel.</param>
    private void StartReading(CancellationToken token) {
        while (!token.IsCancellationRequested) {
            // This call will block until the user provides a key input
            var consoleKeyInfo = Console.ReadKey();

            if (consoleKeyInfo.Key == ConsoleKey.Escape) {
                CurrentInput = "";
                continue;
            }

            if (consoleKeyInfo.Key == ConsoleKey.Backspace) {
                lock (_lock) {
                    var current = CurrentInput;
                    if (current.Length > 0) {
                        // Erase current input inline and redraw shortened input
                        EraseInput(current.Length);

                        current = current.Substring(0, current.Length - 1);
                        CurrentInput = current;

                        Console.Write(current);
                    }
                }

                continue;
            }

            if (consoleKeyInfo.Key == ConsoleKey.Enter) {
                string input;
                lock (_lock) {
                    Clear();

                    input = CurrentInput;
                    CurrentInput = "";
                }

                ConsoleInputEvent?.Invoke(input);
                continue;
            }

            lock (_lock) {
                _currentInput += consoleKeyInfo.KeyChar;
                Console.Write('\r');
                Console.Write(CurrentInput);
            }
        }
    }

    /// <summary>
    /// Writes a line to the console and restores the current input.
    /// </summary>
    /// <param name="line">The line to write.</param>
    public void WriteLine(string line) {
        lock (_lock) {
            var current = CurrentInput; // snapshot current input
            var text = line.TrimEnd('\r', '\n');

            // If the line is empty after trimming, skip writing entirely to avoid blank gaps
            if (text.Length == 0) return;

            // Clear the visible input in-place without moving to a new row
            EraseInput(current.Length);

            Console.WriteLine(text);

            // Restore the input on the same line
            if (current.Length > 0) Console.Write(current);
        }
    }

    /// <summary>
    /// Clears the current input.
    /// </summary>
    private void Clear() {
        EraseInput(CurrentInput.Length);
    }

    /// <summary>
    /// Erases the given number of visible input characters in-place and returns the cursor to the line start.
    /// </summary>
    /// <param name="length">The number of characters to erase.</param>
    private static void EraseInput(int length) {
        if (length == 0) {
            return;
        }

        Console.Write('\r');
        Console.Write(new string(' ', length));
        Console.Write('\r');
    }
}
