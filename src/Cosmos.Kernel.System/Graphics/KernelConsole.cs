using System.Drawing;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace Cosmos.Kernel.System.Graphics;

/// <summary>
/// Cell-based graphics console for kernel output.
/// Uses a character grid (cells) similar to Aura OS for efficient terminal rendering.
/// </summary>
public static class KernelConsole
{
    // Lock for thread-safe console access
    private static Cosmos.Kernel.Core.Scheduler.SpinLock _lock;

    private static Canvas _canvas;

    // Cursor position in character coordinates (column, row)
    private static int _cursorX;
    private static int _cursorY;

    // Terminal dimensions in characters
    private static int _cols;
    private static int _rows;

    // Character dimensions from font
    private static int CharWidth => PCScreenFont.DefaultFont.Width;
    private static int CharHeight => PCScreenFont.DefaultFont.Height;

    // Cell buffer - stores all characters and their colors
    private static Cell[]? _cells;

    // Current colors
    private static uint _foregroundColor = (uint)Color.White.ToArgb();
    private static uint _backgroundColor = (uint)Color.Black.ToArgb();

    // Cursor visibility
    private static bool _cursorVisible = true;
    private static bool _cursorDrawn = false;

    // Initialization state
    private static bool _isInitialized = false;

    // Console color palette (standard 16 colors)
    private static readonly uint[] _palette = new uint[16]
    {
        0xFF000000, // Black
        0xFF000080, // DarkBlue
        0xFF008000, // DarkGreen
        0xFF008080, // DarkCyan
        0xFF800000, // DarkRed
        0xFF808000, // DarkMagenta
        0xFF808000, // DarkYellow
        0xFFC0C0C0, // Gray
        0xFF808080, // DarkGray
        0xFF0000FF, // Blue
        0xFF00FF00, // Green
        0xFF00FFFF, // Cyan
        0xFFFF0000, // Red
        0xFFFF00FF, // Magenta
        0xFFFFFF00, // Yellow
        0xFFFFFFFF  // White
    };

    /// <summary>
    /// Gets whether graphics console is available and initialized.
    /// </summary>
    //public static unsafe bool IsAvailable => _isInitialized && Canvas.Address != null;
    public static unsafe bool IsAvailable => _isInitialized;

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

    public static Canvas Canvas => _canvas;

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
            return false;

        _isInitialized = true;

        _canvas = FullScreenCanvas.GetFullScreenCanvas();    // canvas = GetFullScreenCanvas(start);

        /* Clear the Screen with the color 'Blue' */
        _canvas.Clear(Color.Blue);

        // Calculate terminal dimensions based on font size
        _cols = (int)_canvas.Mode.Width / CharWidth;
        _rows = (int)_canvas.Mode.Height / CharHeight;
        // Allocate cell buffer
        _cells = new Cell[_cols * _rows];

        // Initialize all cells to empty with default colors
        ClearCells();

        // Clear screen
        _canvas.Clear((int)_backgroundColor);
        _canvas.Display();

        return true;
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
        using (InternalCpu.DisableInterruptsScope())
        {
            if (x >= 0 && x < _cols && y >= 0 && y < _rows)
            {
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
                }
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

        _canvas.DrawFilledRectangle(Color.FromArgb((int)_foregroundColor), pixelX, pixelY, CharWidth, 2);
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

        _canvas.DrawFilledRectangle(Color.FromArgb((int)bgColor), pixelX, pixelY, CharWidth, 2);
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
        _canvas.DrawFilledRectangle(Color.FromArgb((int)cell.BackgroundColor), pixelX, pixelY, CharWidth, CharHeight);

        // Draw character if not empty
        if (cell.Char != '\0' && cell.Char != '\n')
        {
            _canvas.DrawChar(cell.Char, PCScreenFont.DefaultFont, Color.FromArgb((int)cell.ForegroundColor), pixelX, pixelY);
        }
    }

    /// <summary>
    /// Redraws the entire screen from the cell buffer.
    /// Thread-safe.
    /// </summary>
    public static void Redraw()
    {
        using (InternalCpu.DisableInterruptsScope())
        {
            if (!IsAvailable || _cells == null)
                return;

            _lock.Acquire();
            try
            {
                RedrawInternal();
            }
            finally
            {
                _lock.Release();
            }
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
        _canvas.Clear((int)_backgroundColor);

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
                    _canvas.DrawChar(cell.Char, PCScreenFont.DefaultFont, Color.FromArgb((int)cell.ForegroundColor), pixelX, pixelY);
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
        using (InternalCpu.DisableInterruptsScope())
        {
            if (!IsAvailable || _cells == null)
                return;

            _lock.Acquire();
            try
            {
                WriteInternal(c);
            }
            finally
            {
                _lock.Release();
            }
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
        using (InternalCpu.DisableInterruptsScope())
        {
            if (!IsAvailable || _cells == null)
                return;

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
            }
        }
    }

    /// <summary>
    /// Writes a Span of character at the current cursor position
    /// </summary>
    /// <param name="buffer">Span of characters to write</param>
    public static void Write(ReadOnlySpan<char> buffer)
    {
        using (InternalCpu.DisableInterruptsScope())
        {
            if (!IsAvailable || _cells == null)
                return;

            _lock.Acquire();
            try
            {
                foreach (char c in buffer)
                {
                    WriteInternal(c);
                }
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    /// <summary>
    /// Writes a character followed by a newline.
    /// Thread-safe.
    /// </summary>
    public static void WriteLine(char c)
    {
        using (InternalCpu.DisableInterruptsScope())
        {
            if (!IsAvailable || _cells == null)
                return;

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
            }
        }
    }

    /// <summary>
    /// Writes a string followed by a newline.
    /// Thread-safe.
    /// </summary>
    public static void WriteLine(string text)
    {
        using (InternalCpu.DisableInterruptsScope())
        {
            if (!IsAvailable || _cells == null)
                return;

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
            }
        }

    }

    /// <summary>
    /// Writes a newline.
    /// Thread-safe.
    /// </summary>
    public static void WriteLine()
    {
        using (InternalCpu.DisableInterruptsScope())
        {
            if (!IsAvailable)
                return;

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
            }
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
        using (InternalCpu.DisableInterruptsScope())
        {
            if (!IsAvailable)
                return;

            _lock.Acquire();
            try
            {
                EraseCursor();
                ClearCells();
                _canvas.Clear((int)_backgroundColor);
                _cursorX = 0;
                _cursorY = 0;
                DrawCursor();
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    /// <summary>
    /// Resets colors to default (white on black).
    /// </summary>
    public static void ResetColors()
    {
        _foregroundColor = (uint)Color.White.ToArgb();
        _backgroundColor = (uint)Color.Black.ToArgb();
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
