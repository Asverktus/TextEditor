using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace TextFileProcessor
{
  #region Exceptions
  public class TextFileException : Exception
  {
    public TextFileException(string message) : base(message) { }
  }

  #endregion

  #region Memento
  public class TextDocumentMemento
  {
    public string content { get; private set; }
    public DateTime timestamp { get; private set; }

    public TextDocumentMemento(string content)
    {
      this.content = content;
      timestamp = DateTime.Now;
    }
  }

  #endregion

  #region TextDocument
  [Serializable]
  public class TextDocument
  {
    public string filePath { get; private set; }
    public string content { get; set; }
    public DateTime lastModified { get; private set; }

    private TextDocument() { }

    public TextDocument(string path)
    {
      if (!File.Exists(path))
        throw new TextFileException($"File not found: {path}");

      filePath = path;
      content = File.ReadAllText(path);
      lastModified = File.GetLastWriteTime(path);
    }

    public TextDocument(string path, string initialContent)
    {
      filePath = path;
      content = initialContent;
      lastModified = DateTime.Now;
      Save();
    }

    public void Save()
    {
      File.WriteAllText(filePath, content);
      lastModified = File.GetLastWriteTime(filePath);
    }

    public TextDocumentMemento CreateMemento()
    {
      return new TextDocumentMemento(content);
    }

    public void RestoreMemento(TextDocumentMemento memento)
    {
      content = memento.content;
    }

    #region Serialization
    public void BinarySerialize(string targetPath)
    {
      using (var stream = new FileStream(targetPath, FileMode.Create))
      using (var writer = new BinaryWriter(stream, Encoding.UTF8))
      {
        writer.Write(filePath ?? "");
        writer.Write(content ?? "");
        writer.Write(lastModified.Ticks);
      }
    }

    public static TextDocument BinaryDeserialize(string path)
    {
      using (var stream = new FileStream(path, FileMode.Open))
      using (var reader = new BinaryReader(stream, Encoding.UTF8))
      {
        var doc = new TextDocument
        {
          filePath = reader.ReadString(),
          content = reader.ReadString(),
          lastModified = new DateTime(reader.ReadInt64())
        };

        return doc;
      }
    }

    public void XmlSerialize(string targetPath)
    {
      using (var writer = new StreamWriter(targetPath))
      {
        new XmlSerializer(typeof(TextDocument)).Serialize(writer, this);
      }
    }

    public static TextDocument XmlDeserialize(string path)
    {
      using (var reader = new StreamReader(path))
      {
        return (TextDocument)new XmlSerializer(typeof(TextDocument)).Deserialize(reader);
      }
    }

    #endregion

    public override string ToString()
    {
      return $"{Path.GetFileName(filePath)}: {content.Length} chars, modified {lastModified:HH:mm:ss}";
    }
  }

  #endregion

  #region Search
  public class SearchResult
  {
    public string filePath { get; private set; }
    public string fileName { get; private set; }
    public string keyword { get; private set; }
    public int occurrences { get; private set; }
    public List<int> lineNumbers { get; private set; }

    public SearchResult(string path, string keyword)
    {
      filePath = path;
      fileName = Path.GetFileName(path);
      this.keyword = keyword;
      occurrences = 0;
      lineNumbers = new List<int>();
    }

    public void AddOccurrence(int lineNumber)
    {
      ++occurrences;
      lineNumbers.Add(lineNumber);
    }

    public override string ToString()
    {
      return $"{fileName}: {occurrences} matches at lines {string.Join(", ", lineNumbers)}";
    }
  }

  public class FileSearcher
  {
    private List<string> _directories;
    private List<string> _extensions;

    public bool searchSubdirectories { get; set; } = true;
    public bool caseSensitive { get; set; } = false;

    public FileSearcher()
    {
      _directories = new List<string>();
      _extensions = new List<string>();
    }

    public void AddDirectory(string path)
    {
      if (!Directory.Exists(path))
        throw new TextFileException($"Directory not found: {path}");

      if (!_directories.Contains(path)) _directories.Add(path);
    }

    public void AddExtension(string ext)
    {
      string normExt = ext.StartsWith(".") ? ext : "." + ext;

      if (!_extensions.Contains(normExt)) _extensions.Add(normExt);
    }

    private IEnumerable<string> GetFiles()
    {
      var files = new List<string>();
      var option = searchSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

      foreach (var dir in _directories)
      {
        if (_extensions.Count == 0)
        {
          files.AddRange(Directory.GetFiles(dir, "*.*", option));
        }

        else
        {
          foreach (var ext in _extensions)
          {
            files.AddRange(Directory.GetFiles(dir, "*" + ext, option));
          }
        }
      }

      return files.Distinct();
    }

    public List<SearchResult> Search(string keyword)
    {
      var results = new List<SearchResult>();

      if (string.IsNullOrWhiteSpace(keyword)) 
        return results;

      string searchKeyword = caseSensitive ? keyword : keyword.ToLower();

      foreach (var file in GetFiles())
      {
        try
        {
          var lines = File.ReadAllLines(file);
          var result = new SearchResult(file, keyword);

          int lineNum = 1;

          foreach (var line in lines)
          {
            string compareLine = caseSensitive ? line : line.ToLower();

            if (compareLine.Contains(searchKeyword)) result.AddOccurrence(lineNum);

            ++lineNum;
          }

          if (result.occurrences > 0) results.Add(result);
        }

        catch { }
      }

      return results;
    }
  }

  #endregion

  #region Editor with Memento
  public class TextEditor
  {
    private TextDocument _document;
    private Stack<TextDocumentMemento> _undoStack;
    private Stack<TextDocumentMemento> _redoStack;

    public bool isDocumentOpen => _document != null;

    public TextEditor()
    {
      _undoStack = new Stack<TextDocumentMemento>();
      _redoStack = new Stack<TextDocumentMemento>();
    }

    public void OpenFile(string path)
    {
      _document = new TextDocument(path);

      SaveState();

      Console.WriteLine($"Opened: {Path.GetFileName(path)}");
    }

    public void CreateFile(string path, string content = "")
    {
      string currentDirBefore = Directory.GetCurrentDirectory();
      Console.WriteLine($"Current directory before: {currentDirBefore}");

      _document = new TextDocument(path, content);
      SaveState();

      string fullPath = Path.GetFullPath(path);

      Console.WriteLine($"Created: {Path.GetFileName(path)}");
      Console.WriteLine($"FULL PATH: {fullPath}");

      if (File.Exists(fullPath))
      {
        Console.WriteLine("File exists at that location");
        Console.WriteLine($"File size: {new FileInfo(fullPath).Length} bytes");
      }

      else
      {
        Console.WriteLine("File DOES NOT exist at that location");
      }
    }

    private void SaveState()
    {
      if (_document == null) 
        return;

      _undoStack.Push(_document.CreateMemento());
      _redoStack.Clear();
    }

    public void EditContent(string newContent)
    {
      if (_document == null)
      {
        Console.WriteLine("No document open");

        return;
      }

      SaveState();

      _document.content = newContent;
    }

    public void AppendText(string text)
    {
      if (_document == null)
      {
        Console.WriteLine("No document open");

        return;
      }

      SaveState();

      _document.content += text;
    }

    public bool Undo()
    {
      if (_undoStack.Count <= 1) 
        return false;

      var current = _undoStack.Pop();

      _redoStack.Push(current);
      _document.RestoreMemento(_undoStack.Peek());

      return true;
    }

    public bool Redo()
    {
      if (_redoStack.Count == 0) 
        return false;

      var redo = _redoStack.Pop();
      _undoStack.Push(redo);
      _document.RestoreMemento(redo);

      return true;
    }

    public void Save()
    {
      if (_document == null)
      {
        Console.WriteLine("No document open");
        return;
      }

      string fullPathBefore = Path.GetFullPath(_document.filePath);

      Console.WriteLine($"Saving to: {fullPathBefore}");

      _document.Save();

      string fullPathAfter = Path.GetFullPath(_document.filePath);

      Console.WriteLine($"Saved successfully to: {fullPathAfter}");

      if (File.Exists(fullPathAfter))
      {
        Console.WriteLine($"File size: {new FileInfo(fullPathAfter).Length} bytes");
      }
    }

    public void ShowContent()
    {
      if (_document == null)
      {
        Console.WriteLine("No document open");

        return;
      }

      Console.WriteLine("\n--- CONTENT ---");
      Console.WriteLine(_document.content);
      Console.WriteLine("--- END ---\n");
    }
  }
  #endregion

  #region Open point with Enum
  public enum MenuCommand
  {
    Exit = 0,
    OpenFile = 1,
    NewFile = 2,
    EditContent = 3,
    AppendText = 4,
    Undo = 5,
    Redo = 6,
    Save = 7,
    ShowContent = 8,
    SearchFiles = 9
  }

  class Program
  {
    static void Main()
    {
      var editor = new TextEditor();
      var searcher = new FileSearcher();
      searcher.AddDirectory(Directory.GetCurrentDirectory());
      searcher.AddExtension(".txt");

      while (true)
      {
        Console.WriteLine(
          "\n=== TEXT EDITOR ===\n" +
          "1. Open file\n2. New file\n3. Edit content\n4. Append text\n5. Undo\n6. Redo\n" +
          "7. Save\n8. Show content\n9. Search files\n0. Exit"
        );

        Console.Write("Choose: ");
        string userInput;
        userInput = Console.ReadLine();

        int choice;

        bool parseResult;
        parseResult = int.TryParse(userInput, out choice);

        if (!parseResult)
        {
          Console.WriteLine("Invalid input. Please enter a number.");

          continue;
        }

        if (choice == (int)MenuCommand.Exit)
        {
          return;
        }

        try
        {
          MenuCommand selectedCommand;
          selectedCommand = (MenuCommand)choice;

          switch (selectedCommand)
          {
            case MenuCommand.OpenFile:

              Console.Write("Path: ");

              string openPath;
              openPath = Console.ReadLine();
              editor.OpenFile(openPath);

              break;

            case MenuCommand.NewFile:

              Console.Write("Path: ");

              string newPath;
              newPath = Console.ReadLine();

              Console.Write("Initial content (optional): ");

              string initialContent;
              initialContent = Console.ReadLine();
              editor.CreateFile(newPath, initialContent);

              break;

            case MenuCommand.EditContent:

              Console.Write("New content: ");

              string newContent;
              newContent = Console.ReadLine();
              editor.EditContent(newContent);

              break;

            case MenuCommand.AppendText:

              Console.Write("Text to append: ");

              string appendText;
              appendText = Console.ReadLine();
              editor.AppendText(appendText);

              break;

            case MenuCommand.Undo:

              bool undoResult;
              undoResult = editor.Undo();

              Console.WriteLine(undoResult ? "Undone" : "Nothing to undo");

              break;

            case MenuCommand.Redo:

              bool redoResult;
              redoResult = editor.Redo();

              Console.WriteLine(redoResult ? "Redone" : "Nothing to redo");

              break;

            case MenuCommand.Save:

              editor.Save();

              break;

            case MenuCommand.ShowContent:

              editor.ShowContent();

              break;

            case MenuCommand.SearchFiles:

              Console.Write("Keyword: ");

              string keyword;
              keyword = Console.ReadLine();

              var results = searcher.Search(keyword);

              Console.WriteLine(results.Any() ? string.Join("\n", results) : "Not found");

              break;

            default:

              Console.WriteLine("Invalid choice");

              break;
          }
        }

        catch (Exception ex)
        {
          Console.WriteLine($"Error: {ex.Message}");
        }
      }
    }
  }

  #endregion
}