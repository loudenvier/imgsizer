using CommandLine;
using PhotoSauce.MagicScaler;
using Spectre.Console;
using Spectre.Console.Json;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;

var start = Stopwatch.GetTimestamp();
var watch = Stopwatch.StartNew();
var escHit = false;
int count = 0, exitCode = 0, remainingFilesCount = 0;
long totalBytes = 0, totalResizedBytes = 0;
StatusContext statusContext;

// Let's welcome our user...
AnsiConsole.Write(new FigletText("imgsizer"));
AnsiConsole.MarkupLine($"Image[gray](re)[/]Sizer [yellow]{DateTime.Now.Year}[/]\r\n");

// We process everything inside a "live" AnsiConsole Status message loop
AnsiConsole.Status()
    .AutoRefresh(true)
    .Spinner(Spinner.Known.Pipe)
    .SpinnerStyle(Style.Parse("green bold"))
    .Start("Starting...", ctx => {
        statusContext = ctx;
        // parse our options and call resize if valid
        Parser.Default.ParseArguments<Options>(args)
            .WithParsed(o => Resize(o))
            .WithNotParsed(_ => exitCode = 1);
    });

if (exitCode == 0) {
    // wrap it up and display ending message
    var remaining = remainingFilesCount == 0 ? "" : $"[white on gray] files remaining [/][yellow on maroon] {remainingFilesCount} [/]";
    AnsiConsole.MarkupLine($"\r\n[white on gray] files processed [/][yellow on green] {count} [/][white on gray] total size [/][yellow on purple] {Size(totalBytes)} [/][white on gray] resized size [/][yellow on navy] {Size(totalResizedBytes)} [/]{remaining}[white on gray] runnnig time [/][yellow on blue] {Stopwatch.GetElapsedTime(start)} [/]");
}

return exitCode;

// END OF PROGRAM

int Resize(Options o) {
    // shows user which options are being used by the program
    AnsiConsole.MarkupLine($"Options: ");
    AnsiConsole.Write(new JsonText(o.ToString()));
    AnsiConsole.WriteLine();
    
    // when input is a single file, just resize it and be done with
    if (File.Exists(o.Source))
        return exitCode = ResizeFile(o.Source, o);

    // get source dir and pattern from Source argument (uses current dir if no directory is passed)
    var dir = Path.GetDirectoryName(o.Source);
    if (string.IsNullOrEmpty(dir))
        dir = Directory.GetCurrentDirectory();
    var filter = Path.GetFileName(o.Source);
    // guarantees destination is a full path
    o.Destination = Path.GetFullPath(o.Destination ?? "");
    // if source == destination this is an "inplace" process 
    var inplace = o.Destination == Path.GetFullPath(dir);

    var remainingJobFiles = new List<string>();
    foreach (var file in EnumerateFiles(dir, filter, o)) {
        // don't process files in the destination folder in case it's a subdir of the source folder
        if (!inplace && o.Destination != null && IsSubDir(Path.GetFullPath(o.Destination), Path.GetFullPath(file)))
            continue;
        // check for <ESC> (to quit and optionally save the job)
        if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape) {
            escHit = true;
            AnsiConsole.MarkupLine($"[black on yellow] <ESC> [/][yellow on black] detected. [/]{(o.HasJob() ? "[gray]Saving job state...[/] " : "")}");
            if (!o.HasJob()) 
                // if no job name was provided just quit processing
                break;
        }
        if (escHit) {
            // if <ESC> was hit just capture each remaining file name without processing (resizing) it
            remainingJobFiles.Add(file);
            statusContext.Status($"[yellow on red] {++remainingFilesCount} [/][red on yellow] files remaining... [/]");
        } else {
            // normal processing (<ESC> was not hit)
            ResizeFile(file, o);
        }
    }
    if (escHit && o.HasJob()) {
        // TODO: Could add some more info from the job (how many files were processed, time taken, etc.)
        File.WriteAllLines(o.Job!, remainingJobFiles);
        AnsiConsole.MarkupLine($"[default]Job saved to: [/][yellow]\"{Path.GetFullPath(o.Job!)}\" [/]");
    } else if (o.HasJob() && File.Exists(o.Job)) {
        // deletes the Job if it existed and the job was completed
        AnsiConsole.MarkupLine("[yellow on green] Job completed! [/]");
        File.Delete(o.Job);
    }
    return 0;
}

int ResizeFile(string filename, Options o) {
    if (!File.Exists(filename)) {
        AnsiConsole.MarkupLine($"[yellow on red] {filename} [/][white on gray] does not exist [/]\r\n");
        return -2;
    }
    watch.Restart();
    var markup = $"[yellow on green] {count} [/][white on gray] files resized [/]{(o.HasJob() ? $"[black on olive] <ESC> to quit and save job [/]" : "")}";
    statusContext.Status(markup);

    var fi = new FileInfo(filename);
    var bytes = fi.Length;
    totalBytes += bytes;

    AnsiConsole.Markup($"[gray]Processing:[/] ");
    AnsiConsole.Write(new TextPath($"{fi.FullName}..."));

    // guarantees destination dir exists and creates the file in its original subdir hierarchy
    var relativeFilename = GetRelativePath(filename, o); // maintain same dir hierarchy
    var dest = Path.Combine(o.Destination ?? "", relativeFilename);
    ForceDirectory(dest);

    // check if it needs to ask to overwrite destination (will compute the size even if skipped)
    var confirmation = o switch 
    { 
        { NeverOverwrite: true }                    => ConfirmResult.No, 
        { Overwrite: false } when File.Exists(dest) => Confirm($"The destination file already exists. Overwrite it? "),
        _                                           => ConfirmResult.Yes,
    };
    switch (confirmation) {
        case ConfirmResult.No:
            AnsiConsole.MarkupLine($"[black on red] File skipped! [/]");
            break;
        case ConfirmResult.Escape:
            escHit = true;
            return -1;
        case ConfirmResult.Never:
            o.NeverOverwrite = true;
            break;
        case ConfirmResult.Always:
            // don't confir to overwrite anymore
            o.Overwrite = true;
            goto case ConfirmResult.Yes;
        case ConfirmResult.Yes: 
            {
                using var stm = new MemoryStream();
                MagicImageProcessor.ProcessImage(filename, stm, new ProcessImageSettings {
                    Width = o.Width ?? 0,
                    Height = o.Height ?? 0,
                    ResizeMode = CropScaleMode.Max,
                });
                stm.Position = 0;
                File.WriteAllBytes(dest, stm.ToArray());
            }
            break;
    }
    var resizedBytes = new FileInfo(dest).Length;
    totalResizedBytes += resizedBytes;

    AnsiConsole.MarkupLine($" [[[blue]done[/]]] [olive] {Size(bytes)} [/]resized to[yellow] {Size(resizedBytes)} [/] [gray]({watch.Elapsed.Milliseconds}ms)[/]");
    count++;
    return 0;
}

IEnumerable<string> EnumerateFiles(string dir, string filter, Options o) {
    // we enumerate the contents of the job file if it exists, or else the directory contents
    if (o.HasJob()) {
        var jobPath = Path.GetFullPath(o.Job!);
        if (File.Exists(o.Job)) {
            AnsiConsole.MarkupLine($"[yellow on green] resuming job [/][yellow on blue] \"{jobPath}\" [/]");
            return File.ReadLines(o.Job);
        } else {
            AnsiConsole.MarkupLine($"[yellow on red] starting job [/][yellow on blue] \"{jobPath}\" [/]");
        }
    }
    var options = new EnumerationOptions {
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.PlatformDefault,
        RecurseSubdirectories = o.Recursive,
        ReturnSpecialDirectories = false,
        MatchType = MatchType.Win32,
    };
    return Directory.EnumerateFiles(dir, filter, options);
}

bool IsSubDir(string parent, string child) {
    var rel = Path.GetRelativePath(parent, child);
    return rel != "."
        && rel != ".."
        && !rel.StartsWith("../")
        && !rel.StartsWith("..\\")
        && !Path.IsPathRooted(rel);
}

void ForceDirectory(string? dirOrFileName) {
    if (dirOrFileName == null) return;
    var dir = Path.GetDirectoryName(dirOrFileName);
    if (dir != null && !Directory.Exists(dir))
        Directory.CreateDirectory(dir);
}

string GetRelativePath(string filename, Options o) =>
    Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(o.Source)) ?? "", Path.GetFullPath(filename));

string Size(long bytes, int unit = 1024) {
    // converts bytes to the "best-fit" unit of measurement (change unit to 1000 to use the new 1000 B =1 MB storage standard)
    if (bytes < unit) return $"{bytes} B"; 
    var exp = (int)(Math.Log(bytes) / Math.Log(unit));
    return $"{bytes / Math.Pow(unit, exp):F2} {("KMGTPE")[exp - 1]}B";
}

ConfirmResult Confirm(string msg) {
    AnsiConsole.MarkupLine($"[yellow] {msg} [/]([yellow on green]<Y>[/]es, [yellow on red]<N>[/]o, [yellow on blue]<A>[/]lways, n[yellow on purple]<E>[/]ver, [black on yellow]<ESC>[/])");
    ConsoleKey key;
    while (ConfirmKeys.Map.ContainsKey(key = Console.ReadKey(true).Key)) { }
    AnsiConsole.MarkupLine($"[black on white] {key} [/][white on gray] was pressed. [/]");
    return ConfirmKeys.Map[key];
}

/// <summary>
/// The options that the program accepts. Automatically mapped to command line arguments.
/// </summary>
public class Options
{
    public Options() { }
    [Value(0, Required = true, MetaName = "source", HelpText = "Source file/pattern")]
    public string Source { get; set; } = "";
    [Option('w', "width", HelpText = "Image's target width")]
    public int? Width { get; set; }
    [Option('h', "height", HelpText = "Image's target height")]
    public int? Height { get; set; }
    [Option('d', "dest", Default = "resized", HelpText = "Output path/directory (if it's '.' the source files will be overwritten)")]
    public string? Destination { get; set; } 
    [Option('o', "overwrite", Default = false, HelpText = "Automatically overwrites destination files")]
    public bool Overwrite { get; set; }
    public bool NeverOverwrite { get; set; }
    [Option('r', "recursive", Default = false, HelpText = "Includes files inside subdirectories")]
    public bool Recursive { get; set; }
    [Option('j', "job", Default = null, HelpText = "Allows pausing and resuming with named \"jobs\"")]
    public string? Job { get; set; }

    public bool HasJob() => !String.IsNullOrWhiteSpace(Job);

    public override string ToString() => JsonSerializer.Serialize(this);
}

enum ConfirmResult { Yes, No, Always, Never, Escape }

static class ConfirmKeys
{
    public static Dictionary<ConsoleKey, ConfirmResult> Map { get; } = new() {
        [ConsoleKey.Y] = ConfirmResult.Yes,
        [ConsoleKey.N] = ConfirmResult.No,
        [ConsoleKey.A] = ConfirmResult.Always,
        [ConsoleKey.E] = ConfirmResult.Never,
        [ConsoleKey.Escape] = ConfirmResult.Escape,
    };
}