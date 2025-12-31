using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Graphics.Fonts;

namespace Cosmos.Kernel.Graphics;

/// <summary>
/// Cell-based graphics console for kernel output.
/// Uses a character grid (cells) similar to Aura OS for efficient terminal rendering.
/// </summary>
public static class KernelConsole
{
    // Lock for thread-safe console access
    private static Cosmos.Kernel.Core.Scheduler.SpinLock _lock;

    // Cursor position in character coordinates (column, row)
    private static int _cursorX;
    private static int _cursorY;

    // Terminal dimensions in characters
    private static int _cols;
    private static int _rows;

    // Character dimensions from font
    private static int CharWidth => PCScreenFont.CharWidth;
    private static int CharHeight => PCScreenFont.CharHeight;

    // Cell buffer - stores all characters and their colors
    private static Cell[]? _cells;

    // Current colors
    private static uint _foregroundColor = Color.White;
    private static uint _backgroundColor = Color.Black;

    // Cursor visibility
    private static bool _cursorVisible = true;
    private static bool _cursorDrawn = false;

    // Initialization state
    private static bool _isInitialized = false;

    // Console color palette (standard 16 colors)
    private static readonly uint[] _palette = new uint[16]
    {
        0x000000, // Black
        0x000080, // DarkBlue
        0x008000, // DarkGreen
        0x008080, // DarkCyan
        0x800000, // DarkRed
        0x800080, // DarkMagenta
        0x808000, // DarkYellow
        0xC0C0C0, // Gray
        0x808080, // DarkGray
        0x0000FF, // Blue
        0x00FF00, // Green
        0x00FFFF, // Cyan
        0xFF0000, // Red
        0xFF00FF, // Magenta
        0xFFFF00, // Yellow
        0xFFFFFF  // White
    };

    /// <summary>
    /// Gets whether graphics console is available and initialized.
    /// </summary>
    public static unsafe bool IsAvailable => _isInitialized && Canvas.Address != null;

    /// <summary>
    /// Gets whether the graphics console has been initialized.
    /// </summary>
    public static bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets or sets the cursor X position (column).
    /// </summary>
    public static int CursorX
    {
        get => _cursorX;
        set
        {
            if (value >= 0 && value < _cols)
            {
                EraseCursor();
                _cursorX = value;
                DrawCursor();
            }
        }
    }

    /// <summary>
    /// Gets or sets the cursor Y position (row).
    /// </summary>
    public static int CursorY
    {
        get => _cursorY;
        set
        {
            if (value >= 0 && value < _rows)
            {
                EraseCursor();
                _cursorY = value;
                DrawCursor();
            }
        }
    }

    /// <summary>
    /// Gets the number of columns in the terminal.
    /// </summary>
    public static int Cols => _cols;

    /// <summary>
    /// Gets the number of rows in the terminal.
    /// </summary>
    public static int Rows => _rows;

    /// <summary>
    /// Gets or sets whether the cursor is visible.
    /// </summary>
    public static bool CursorVisible
    {
        get => _cursorVisible;
        set
        {
            if (_cursorVisible != value)
            {
                if (_cursorVisible)
                    EraseCursor();
                _cursorVisible = value;
                if (_cursorVisible)
                    DrawCursor();
            }
        }
    }

    /// <summary>
    /// Gets or sets the foreground color.
    /// </summary>
    public static uint ForegroundColor
    {
        get => _foregroundColor;
        set => _foregroundColor = value;
    }

    /// <summary>
    /// Gets or sets the background color.
    /// </summary>
    public static uint BackgroundColor
    {
        get => _backgroundColor;
        set => _backgroundColor = value;
    }

    /// <summary>
    /// Sets the foreground color from ConsoleColor enum.
    /// </summary>
    public static void SetForegroundColor(ConsoleColor color)
    {
        _foregroundColor = _palette[(int)color];
    }

    /// <summary>
    /// Sets the background color from ConsoleColor enum.
    /// </summary>
    public static void SetBackgroundColor(ConsoleColor color)
    {
        _backgroundColor = _palette[(int)color];
    }

    /// <summary>
    /// Converts ConsoleColor to uint color.
    /// </summary>
    public static uint ConsoleColorToUint(ConsoleColor color)
    {
        return _palette[(int)color];
    }

    /// <summary>
    /// Initializes the graphics framebuffer and console.
    /// </summary>
    public static unsafe bool Initialize()
    {
        if (_isInitialized)
            return Canvas.Address != null;

        _isInitialized = true;

        if (Limine.Framebuffer.Response != null && Limine.Framebuffer.Response->FramebufferCount > 0)
        {
            LimineFramebuffer* fb = Limine.Framebuffer.Response->Framebuffers[0];
            Canvas.Address = (uint*)fb->Address;
            Canvas.Width = (uint)fb->Width;
            Canvas.Height = (uint)fb->Height;
            Canvas.Pitch = (uint)fb->Pitch;

            // Calculate terminal dimensions based on font size
            _cols = (int)Canvas.Width / CharWidth;
            _rows = (int)Canvas.Height / CharHeight;

            // Allocate cell buffer
            _cells = new Cell[_cols * _rows];

            // Initialize all cells to empty with default colors
            ClearCells();

            // Clear screen
            Canvas.ClearScreen(_backgroundColor);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the cell index for a given row and column.
    /// </summary>
    private static int GetIndex(int row, int col)
    {
        return row * _cols + col;
    }

    /// <summary>
    /// Clears all cells to empty with current colors.
    /// </summary>
    private static void ClearCells()
    {
        if (_cells == null) return;

        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i] = Cell.Empty(_foregroundColor, _backgroundColor);
        }
    }

    /// <summary>
    /// Sets the cursor position.
    /// Thread-safe.
    /// </summary>
    public static void SetCursorPosition(int x, int y)
    {
        if (x >= 0 && x < _cols && y >= 0 && y < _rows)
        {
            InternalCpu.DisableInterrupts();
            _lock.Acquire();
            try
            {
                EraseCursor();
                _cursorX = x;
                _cursorY = y;
                DrawCursor();
            }
            finally
            {
                _lock.Release();
                InternalCpu.EnableInterrupts();
            }
        }
    }

    /// <summary>
    /// Draws the cursor at the current position.
    /// </summary>
    private static void DrawCursor()
    {
        if (!IsAvailable || !_cursorVisible || _cursorDrawn)
            return;

        // Draw cursor as an underline bar at the bottom of the character cell
        int pixelX = _cursorX * CharWidth;
        int pixelY = _cursorY * CharHeight + CharHeight - 2;

        Canvas.DrawRectangle(_foregroundColor, pixelX, pixelY, CharWidth, 2);
        _cursorDrawn = true;
    }

    /// <summary>
    /// Erases the cursor at the current position.
    /// </summary>
    private static void EraseCursor()
    {
        if (!IsAvailable || !_cursorDrawn)
            return;

        // Erase cursor by redrawing background
        int pixelX = _cursorX * CharWidth;
        int pixelY = _cursorY * CharHeight + CharHeight - 2;

        // Get the background color of the current cell
        uint bgColor = _backgroundColor;
        if (_cells != null && _cursorY < _rows && _cursorX < _cols)
        {
            int index = GetIndex(_cursorY, _cursorX);
            bgColor = _cells[index].BackgroundColor;
        }

        Canvas.DrawRectangle(bgColor, pixelX, pixelY, CharWidth, 2);
        _cursorDrawn = false;
    }

    /// <summary>
    /// Draws a character at a specific cell position.
    /// </summary>
    private static void DrawCharAt(int col, int row)
    {
        if (!IsAvailable || _cells == null)
            return;

        int index = GetIndex(row, col);
        if (index < 0 || index >= _cells.Length)
            return;

        ref Cell cell = ref _cells[index];
        int pixelX = col * CharWidth;
        int pixelY = row * CharHeight;

        // Draw background
        Canvas.DrawRectangle(cell.BackgroundColor, pixelX, pixelY, CharWidth, CharHeight);

        // Draw character if not empty
        if (cell.Char != '\0' && cell.Char != '\n')
        {
            PCScreenFont.PutChar(cell.Char, pixelX, pixelY, cell.ForegroundColor, cell.BackgroundColor);
        }
    }

    /// <summary>
    /// Redraws the entire screen from the cell buffer.
    /// Thread-safe.
    /// </summary>
    public static void Redraw()
    {
        if (!IsAvailable || _cells == null)
            return;

        InternalCpu.DisableInterrupts();
        _lock.Acquire();
        try
        {
            RedrawInternal();
        }
        finally
        {
            _lock.Release();
            InternalCpu.EnableInterrupts();
        }
    }

    /// <summary>
    /// Internal redraw (must be called with lock held).
    /// </summary>
    private static void RedrawInternal()
    {
        if (_cells == null)
            return;

        EraseCursor();

        // Clear screen with background color
        Canvas.ClearScreen(_backgroundColor);

        // Draw all cells
        for (int row = 0; row < _rows; row++)
        {
            for (int col = 0; col < _cols; col++)
            {
                int index = GetIndex(row, col);
                ref Cell cell = ref _cells[index];

                if (cell.Char != '\0' && cell.Char != '\n')
                {
                    int pixelX = col * CharWidth;
                    int pixelY = row * CharHeight;
                    PCScreenFont.PutChar(cell.Char, pixelX, pixelY, cell.ForegroundColor, cell.BackgroundColor);
                }
            }
        }

        DrawCursor();
    }

    /// <summary>
    /// Writes a character at the current cursor position.
    /// Thread-safe: uses spinlock with interrupt protection.
    /// </summary>
    public static void Write(char c)
    {
        if (!IsAvailable || _cells == null)
            return;

        InternalCpu.DisableInterrupts();
        _lock.Acquire();
        try
        {
            WriteInternal(c);
        }
        finally
        {
            _lock.Release();
            InternalCpu.EnableInterrupts();
        }
    }

    /// <summary>
    /// Internal write implementation (must be called with lock held).
    /// </summary>
    private static void WriteInternal(char c)
    {
        EraseCursor();

        switch (c)
        {
            case '\n':
                DoLineFeed();
                break;
            case '\r':
                DoCarriageReturn();
                break;
            case '\t':
                DoTabInternal();
                break;
            case '\b':
                DoBackspace();
                break;
            default:
                // Write character to cell buffer
                int index = GetIndex(_cursorY, _cursorX);
                _cells![index] = new Cell(c, _foregroundColor, _backgroundColor);

                // Draw the character
                DrawCharAt(_cursorX, _cursorY);

                // Advance cursor
                _cursorX++;
                if (_cursorX >= _cols)
                {
                    DoLineFeed();
                }
                break;
        }

        DrawCursor();
    }

    /// <summary>
    /// Internal tab (called with lock held, avoids recursive Write).
    /// </summary>
    private static void DoTabInternal()
    {
        int spaces = 4 - (_cursorX % 4);
        for (int i = 0; i < spaces; i++)
        {
            WriteInternal(' ');
        }
    }

    /// <summary>
    /// Writes a string at the current cursor position.
    /// Thread-safe: uses spinlock with interrupt protection.
    /// </summary>
    public static void Write(string text)
    {
        if (!IsAvailable || _cells == null)
            return;

        InternalCpu.DisableInterrupts();
        _lock.Acquire();
        try
        {
            foreach (char c in text)
            {
                WriteInternal(c);
            }
        }
        finally
        {
            _lock.Release();
            InternalCpu.EnableInterrupts();
        }
    }

    /// <summary>
    /// Writes a character followed by a newline.
    /// Thread-safe.
    /// </summary>
    public static void WriteLine(char c)
    {
        if (!IsAvailable || _cells == null)
            return;

        InternalCpu.DisableInterrupts();
        _lock.Acquire();
        try
        {
            WriteInternal(c);
            EraseCursor();
            DoLineFeed();
            DrawCursor();
        }
        finally
        {
            _lock.Release();
            InternalCpu.EnableInterrupts();
        }
    }

    /// <summary>
    /// Writes a string followed by a newline.
    /// Thread-safe.
    /// </summary>
    public static void WriteLine(string text)
    {
        if (!IsAvailable || _cells == null)
            return;

        InternalCpu.DisableInterrupts();
        _lock.Acquire();
        try
        {
            foreach (char c in text)
            {
                WriteInternal(c);
            }
            EraseCursor();
            DoLineFeed();
            DrawCursor();
        }
        finally
        {
            _lock.Release();
            InternalCpu.EnableInterrupts();
        }
    }

    /// <summary>
    /// Writes a newline.
    /// Thread-safe.
    /// </summary>
    public static void WriteLine()
    {
        if (!IsAvailable)
            return;

        InternalCpu.DisableInterrupts();
        _lock.Acquire();
        try
        {
            EraseCursor();
            DoLineFeed();
            DrawCursor();
        }
        finally
        {
            _lock.Release();
            InternalCpu.EnableInterrupts();
        }
    }

    /// <summary>
    /// Performs a line feed (move to next line, column 0).
    /// </summary>
    private static void DoLineFeed()
    {
        _cursorX = 0;
        _cursorY++;

        if (_cursorY >= _rows)
        {
            Scroll();
            _cursorY = _rows - 1;
        }
    }

    /// <summary>
    /// Performs a carriage return (move to column 0).
    /// </summary>
    private static void DoCarriageReturn()
    {
        _cursorX = 0;
    }

    /// <summary>
    /// Performs a backspace (move cursor back and clear character).
    /// </summary>
    private static void DoBackspace()
    {
        if (_cursorX > 0)
        {
            _cursorX--;
        }
        else if (_cursorY > 0)
        {
            // Move to end of previous line
            _cursorY--;
            _cursorX = _cols - 1;
        }

        // Clear the character at cursor position
        int index = GetIndex(_cursorY, _cursorX);
        _cells![index] = Cell.Empty(_foregroundColor, _backgroundColor);
        DrawCharAt(_cursorX, _cursorY);
    }

    /// <summary>
    /// Deletes the character at the cursor position and shifts remaining characters left.
    /// </summary>
    public static void Delete()
    {
        if (!IsAvailable || _cells == null)
            return;

        EraseCursor();

        // Shift all characters on the current line left by one
        int row = _cursorY;
        for (int col = _cursorX; col < _cols - 1; col++)
        {
            int currentIndex = GetIndex(row, col);
            int nextIndex = GetIndex(row, col + 1);
            _cells[currentIndex] = _cells[nextIndex];
            DrawCharAt(col, row);
        }

        // Clear the last cell in the row
        int lastIndex = GetIndex(row, _cols - 1);
        _cells[lastIndex] = Cell.Empty(_foregroundColor, _backgroundColor);
        DrawCharAt(_cols - 1, row);

        DrawCursor();
    }

    /// <summary>
    /// Moves the cursor left by one position.
    /// </summary>
    public static void MoveCursorLeft()
    {
        if (_cursorX > 0)
        {
            EraseCursor();
            _cursorX--;
            DrawCursor();
        }
    }

    /// <summary>
    /// Moves the cursor right by one position.
    /// </summary>
    public static void MoveCursorRight()
    {
        if (_cursorX < _cols - 1)
        {
            EraseCursor();
            _cursorX++;
            DrawCursor();
        }
    }

    /// <summary>
    /// Moves the cursor up by one position.
    /// </summary>
    public static void MoveCursorUp()
    {
        if (_cursorY > 0)
        {
            EraseCursor();
            _cursorY--;
            DrawCursor();
        }
    }

    /// <summary>
    /// Moves the cursor down by one position.
    /// </summary>
    public static void MoveCursorDown()
    {
        if (_cursorY < _rows - 1)
        {
            EraseCursor();
            _cursorY++;
            DrawCursor();
        }
    }

    /// <summary>
    /// Moves the cursor to the beginning of the current line.
    /// </summary>
    public static void MoveCursorHome()
    {
        EraseCursor();
        _cursorX = 0;
        DrawCursor();
    }

    /// <summary>
    /// Moves the cursor to the end of the current line (last non-empty character).
    /// </summary>
    public static void MoveCursorEnd()
    {
        if (_cells == null) return;

        EraseCursor();

        // Find the last non-empty character on the current line
        int lastNonEmpty = 0;
        for (int col = 0; col < _cols; col++)
        {
            int index = GetIndex(_cursorY, col);
            if (_cells[index].Char != '\0')
            {
                lastNonEmpty = col + 1;
            }
        }

        _cursorX = Math.Min(lastNonEmpty, _cols - 1);
        DrawCursor();
    }

    /// <summary>
    /// Scrolls the terminal up by one line.
    /// Must be called with lock held.
    /// </summary>
    private static void Scroll()
    {
        if (_cells == null)
            return;

        // Shift all rows up by one
        for (int row = 0; row < _rows - 1; row++)
        {
            for (int col = 0; col < _cols; col++)
            {
                int currentIndex = GetIndex(row, col);
                int nextIndex = GetIndex(row + 1, col);
                _cells[currentIndex] = _cells[nextIndex];
            }
        }

        // Clear the last row
        for (int col = 0; col < _cols; col++)
        {
            int index = GetIndex(_rows - 1, col);
            _cells[index] = Cell.Empty(_foregroundColor, _backgroundColor);
        }

        // Redraw the entire screen (lock already held)
        RedrawInternal();
    }

    /// <summary>
    /// Clears the entire screen.
    /// Thread-safe.
    /// </summary>
    public static void Clear()
    {
        if (!IsAvailable)
            return;

        InternalCpu.DisableInterrupts();
        _lock.Acquire();
        try
        {
            EraseCursor();
            ClearCells();
            Canvas.ClearScreen(_backgroundColor);
            _cursorX = 0;
            _cursorY = 0;
            DrawCursor();
        }
        finally
        {
            _lock.Release();
            InternalCpu.EnableInterrupts();
        }
    }

    /// <summary>
    /// Clears from the cursor to the end of the current line.
    /// </summary>
    public static void ClearToEndOfLine()
    {
        if (!IsAvailable || _cells == null)
            return;

        EraseCursor();

        for (int col = _cursorX; col < _cols; col++)
        {
            int index = GetIndex(_cursorY, col);
            _cells[index] = Cell.Empty(_foregroundColor, _backgroundColor);
            DrawCharAt(col, _cursorY);
        }

        DrawCursor();
    }

    /// <summary>
    /// Clears the current line entirely.
    /// </summary>
    public static void ClearLine()
    {
        if (!IsAvailable || _cells == null)
            return;

        EraseCursor();

        for (int col = 0; col < _cols; col++)
        {
            int index = GetIndex(_cursorY, col);
            _cells[index] = Cell.Empty(_foregroundColor, _backgroundColor);
            DrawCharAt(col, _cursorY);
        }

        _cursorX = 0;
        DrawCursor();
    }

    /// <summary>
    /// Resets colors to default (white on black).
    /// </summary>
    public static void ResetColors()
    {
        _foregroundColor = Color.White;
        _backgroundColor = Color.Black;
    }

    /// <summary>
    /// Gets the character at the specified position.
    /// </summary>
    public static char GetCharAt(int col, int row)
    {
        if (_cells == null || col < 0 || col >= _cols || row < 0 || row >= _rows)
            return '\0';

        int index = GetIndex(row, col);
        return _cells[index].Char;
    }

    /// <summary>
    /// Gets the cell at the specified position.
    /// </summary>
    public static Cell GetCellAt(int col, int row)
    {
        if (_cells == null || col < 0 || col >= _cols || row < 0 || row >= _rows)
            return Cell.Empty(_foregroundColor, _backgroundColor);

        int index = GetIndex(row, col);
        return _cells[index];
    }

    /// <summary>
    /// Sets the cell at the specified position.
    /// </summary>
    public static void SetCellAt(int col, int row, Cell cell)
    {
        if (_cells == null || col < 0 || col >= _cols || row < 0 || row >= _rows)
            return;

        int index = GetIndex(row, col);
        _cells[index] = cell;
        DrawCharAt(col, row);
    }
}
