﻿using CommandLine;
using PhotoSauce.MagicScaler;
using PhotoSauce.NativeCodecs.Libjpeg;
using PhotoSauce.NativeCodecs.Libpng;
using Spectre.Console;
using static Spectre.Console.AnsiConsole;
using Spectre.Console.Json;
using System.Diagnostics;
using System.Text.Json;
using ByteSizeLib;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;

var start = Stopwatch.GetTimestamp();
var watch = Stopwatch.StartNew();
var options = new Options();
var escHit = false;
int count = 0, exitCode = 0, remainingFilesCount = 0;
long totalBytes = 0, totalResizedBytes = 0;
StatusContext statusContext;

// Need to add our ByteSize TypeConverter for proper command line parsing
TypeDescriptor.AddAttributes(typeof(ByteSize), new TypeConverterAttribute(typeof(ByteSizeTypeConverter)));

// Let's welcome our user...
Write(new FigletText("imgsizer"));
MarkupLine($"                            Image[gray](re)[/]Sizer [yellow]{DateTime.Now.Year}[/]\r\n");

// We process everything inside a "live" AnsiConsole Status message loop
Status()
    .AutoRefresh(true)
    .Spinner(Spinner.Known.Pipe)
    .SpinnerStyle(Style.Parse("green bold"))
    .Start("Starting...", ctx => {
        statusContext = ctx;
        // parse our options and call resize if valid
        var parser = new Parser(cfg => {
            cfg.CaseInsensitiveEnumValues = true;
            cfg.AutoHelp = true;
            cfg.HelpWriter = System.Console.Out;
        });
        parser.ParseArguments<Options>(args)
            .WithParsed(o => Resize(options = o))
            .WithNotParsed(e => exitCode = 1);
    });

if (exitCode == 0) {
    // wrap it up and display ending message
    if (options.CreateJobOnly) {
        MarkupLine($"\r\n[black on gray] job created with [/][yellow on green] {remainingFilesCount} [/][black on gray] files [/]");
    } else {
        var remaining = remainingFilesCount == 0 ? "" : $"[black on gray] files remaining [/][yellow on maroon] {remainingFilesCount} [/]";
        MarkupLine(
            $"\r\n[black on gray] files processed [/][yellow on green] {count} [/]" +
            $"[black on gray] total size [/][yellow on purple] {Size(totalBytes)} [/]" +
            $"[black on gray] resized size [/][yellow on maroon] {Size(totalResizedBytes)} [/]{remaining}" +
            $"[black on gray] runnnig time [/][yellow on darkblue] {Stopwatch.GetElapsedTime(start)} [/]");
    }
}

return exitCode;

// END OF PROGRAM

void Resize(Options o) {
    // if source is a directory will add *.* to the pattern
    if (Directory.Exists(o.Source))
        o.Source = Path.Combine(o.Source, "*.*");
    // get source dir and pattern from Source argument (uses current dir if no directory is passed)
    var dir = Path.GetDirectoryName(o.Source);
    if (string.IsNullOrEmpty(dir))
        dir = Directory.GetCurrentDirectory();
    var filter = Path.GetFileName(o.Source);
    // guarantees destination is a full path
    o.Destination = Path.GetFullPath(o.Destination ?? dir);
    // if source == destination this is an "inplace" process 
    o.Inplace = o.Destination == Path.GetFullPath(dir);
    // constraining quality to the 1..100 range
    o.Quality = Math.Max(1, Math.Min(100, o.Quality));

    ConfigureEncoderOptions(o);

    // shows user which options are being used by the program
    MarkupLine($"Options: ");
    Write(new JsonText(o.ToString()));
    WriteLine();

    // when input is a single file, just resize it and be done with
    if (File.Exists(o.Source)) {
        ResizeFile(o.Source, o);
        return;
    }

    var remainingJobFiles = new List<string>();
    foreach (var file in EnumerateFiles(dir, filter, o)) {
        // don't process files in the destination folder in case it's a subdir of the source folder
        if (!o.Inplace && o.Destination != null && IsSubDir(Path.GetFullPath(o.Destination), Path.GetFullPath(file)))
            continue;
        // check for <ESC> (to quit and optionally save the job)
        if (System.Console.KeyAvailable && System.Console.ReadKey(true).Key == ConsoleKey.Escape) {
            escHit = true;
            MarkupLine($"[black on yellow] <ESC> [/][yellow on black] detected. [/]{(o.HasJob() ? "[gray]Saving job state...[/] " : "")}");
            if (!o.HasJob()) 
                // if no job name was provided just quit processing
                break;
        }
        if (escHit || o.CreateJobOnly) {
            // if <ESC> was hit just capture each remaining file name without processing (resizing) it
            remainingJobFiles.Add(file);
            statusContext.Status($"[yellow on red] {++remainingFilesCount} [/][red on yellow] files remaining... [/]");
        } else {
            // normal processing (<ESC> was not hit)
            ResizeFile(file, o);
        }
    }
    if ((o.CreateJobOnly || escHit) && o.HasJob()) {
        // TODO: Could add some more info from the job (how many files were processed, time taken, etc.)
        File.WriteAllLines(o.Job!, remainingJobFiles);
        MarkupLine($"[default]Job saved to: [/][yellow]\"{Path.GetFullPath(o.Job!)}\" [/]");
    } else if (o.HasJob() && File.Exists(o.Job)) {
        // deletes the Job if it existed and the job was completed
        MarkupLine("[yellow on green] Job completed! [/]");
        File.Delete(o.Job);
    }
}

void ConfigureEncoderOptions(Options o) {
    CodecManager.Configure(codecs => {
        codecs.UseLibjpeg();
        codecs.UseLibpng();

        var jpeg = codecs.OfType<IImageEncoderInfo>().First(c => c.MimeTypes.FirstOrDefault() == ImageMimeTypes.Jpeg);

        // change the default options on the existing definition
        var prop = jpeg.GetType().GetProperty(nameof(IImageEncoderInfo.DefaultOptions));
        prop!.SetValue(jpeg, new JpegOptimizedEncoderOptions(
            Quality: o.Quality, 
            Subsample: o.ChromaSubsampleMode, 
            Progressive: o.JpegProgressiveMode));
    });
}

void ResizeFile(string filename, Options o) {
    if (!File.Exists(filename)) {
        MarkupLine($"[yellow on red] {filename} [/][white on gray] does not exist [/]\r\n");
        exitCode = -2;
        return;
    }
    watch.Restart();
    var markup = $"[yellow on green] {count} [/][white on gray] files resized [/]{(o.HasJob() ? $"[black on olive] <ESC> to quit and save job [/]" : "")}";
    statusContext.Status(markup);

    var fi = new FileInfo(filename);
    var bytes = fi.Length;
    totalBytes += bytes;

    Markup($"[gray]Processing:[/] ");
    Write(new TextPath($"{fi.FullName}..."));

    long resizedBytes = bytes;
    if (!o.SizeThreshold.HasValue || bytes >= o.SizeThreshold.Value.Bytes) {

        // guarantees destination dir exists
        var relativeFilename = GetRelativePath(filename, o); // maintains same dir hierarchy
        var dest = Path.Combine(o.Destination ?? "", relativeFilename);
        if (!o.WhatIf)
            ForceDirectory(dest);

        // check if it needs to ask to overwrite destination (will compute the size even if skipped)
        var confirmation = o switch { { WhatIf: true } => ConfirmResult.Yes, { NeverOverwrite: true } => ConfirmResult.No, { Overwrite: false } when File.Exists(dest) => Confirm($"The destination file already exists. Overwrite it? "),
            _ => ConfirmResult.Yes,
        };
        switch (confirmation) {
            case ConfirmResult.No:
                MarkupLine($"[black on red] File skipped! [/]");
                break;
            case ConfirmResult.Escape:
                escHit = true;
                return;
            case ConfirmResult.Never:
                o.NeverOverwrite = true;
                break;
            case ConfirmResult.Always:
                // don't confirm to overwrite anymore
                o.Overwrite = true;
                goto case ConfirmResult.Yes;
            case ConfirmResult.Yes: {
                    using Stream stm = o.Inplace || o.WhatIf ? new MemoryStream() : new FileStream(dest, FileMode.Create);
                    try {
                        MagicImageProcessor.ProcessImage(filename, stm, new ProcessImageSettings {
                            Width = o.Width ?? 0,
                            Height = o.Height ?? 0,
                            ResizeMode = o.ResizeMode,
                            Anchor = o.Anchor,
                            HybridMode = o.ScaleMode,
                            MatteColor = o.BackColor,
                        });
                        if (!o.WhatIf) {
                            if (stm is MemoryStream mem) {
                                stm.Position = 0;
                                File.WriteAllBytes(dest, mem.ToArray());
                            }
                        } else {
                            // if it's whatif :-) resized bytes will equal the memory stream length
                            resizedBytes = stm.Length;
                        }
                    } catch (Exception e) {
                        resizedBytes = 0;
                        MarkupLine($" [yellow on red] ERROR [/]: [red] {e.Message} [/]");
                    }
                    stm.Flush();
                    stm.Close();
                }
                break;
        }
        if (!o.WhatIf)
            // if not "whatif" resized bytes will be the actual resulting file length
            resizedBytes = new FileInfo(dest).Length;
        MarkupLine($" [[[blue]done[/]]] [olive] {Size(bytes)} [/]resized to[yellow] {Size(resizedBytes)} [/] [gray]({watch.Elapsed.Milliseconds}ms)[/]");
    } else {
        MarkupLine($" [[[purple]skipped[/]]] [olive] {Size(bytes)} [/] [gray]({watch.Elapsed.Milliseconds}ms)[/]");
    }
    totalResizedBytes += resizedBytes;

    count++;
}

IEnumerable<string> EnumerateFiles(string dir, string filter, Options o) {
    // we enumerate the contents of the job file if it exists, or else the directory contents
    if (o.HasJob()) {
        var jobPath = Path.GetFullPath(o.Job!);
        if (File.Exists(o.Job) && !o.CreateJobOnly) {
            MarkupLine($"[yellow on green] resuming job [/][yellow on blue] \"{jobPath}\" [/]");
            return File.ReadLines(o.Job);
        } else {
            MarkupLine($"[yellow on red] {(o.CreateJobOnly ? "creating" : "starting")} job [/][yellow on blue] \"{jobPath}\" [/]");
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

// converts bytes to the "best-fit" unit of measurement 
string Size(long bytes) => ByteSize.FromBytes(bytes).ToString();

ConfirmResult Confirm(string msg) {
    MarkupLine($"[yellow] {msg} [/]([yellow on green]<Y>[/]es, [yellow on red]<N>[/]o, [yellow on blue]<A>[/]lways, n[yellow on purple]<E>[/]ver, [black on yellow]<ESC>[/])");
    ConsoleKey key;
    while (!ConfirmKeys.Map.ContainsKey(key = System.Console.ReadKey(true).Key)) { }
    MarkupLine($"[black on white] {key} [/][white on gray] was pressed. [/]");
    return ConfirmKeys.Map[key];
}

enum ConfirmResult { Yes, No, Always, Never, Escape }

static class ConfirmKeys
{
    internal readonly static Dictionary<ConsoleKey, ConfirmResult> Map = new() {
        [ConsoleKey.Y] = ConfirmResult.Yes,
        [ConsoleKey.N] = ConfirmResult.No,
        [ConsoleKey.A] = ConfirmResult.Always,
        [ConsoleKey.E] = ConfirmResult.Never,
        [ConsoleKey.Escape] = ConfirmResult.Escape,
    };
}

/// <summary>
/// The options that the program accepts. Automatically mapped to command line arguments.
/// </summary>
public class Options
{
    public Options() { }
    [Value(0, Required = true, MetaName = "source", HelpText = "Source file/pattern")]
    public string Source { get; set; } = "";
    public bool Inplace { get; set; }
    [Option('w', "width", HelpText = "Image's target width")]
    public int? Width { get; set; }
    [Option('h', "height", HelpText = "Image's target height")]
    public int? Height { get; set; }
    [Option('d', "dest", HelpText = "Output path/directory (if omitted will perform inplace resizing)")]
    public string? Destination { get; set; } 
    [Option('o', "overwrite", Default = false, HelpText = "Automatically overwrites destination files")]
    public bool Overwrite { get; set; }
    public bool NeverOverwrite { get; set; }
    [Option('r', "recursive", Default = false, HelpText = "Includes files inside subdirectories")]
    public bool Recursive { get; set; }
    [Option('j', "job", Default = null, HelpText = "Allows pausing and resuming with named \"jobs\"")]
    public string? Job { get; set; }
    [Option("createjob", Default = false, HelpText = "Only creates the job by enumerating the files to be processed")]
    public bool CreateJobOnly { get; set; }
    [Option('m', "mode", Default = CropScaleMode.Max, HelpText = "Specifies the resize/crop scale mode to use (Crop, Contain, Stretch, Pad, Max)")]
    public CropScaleMode ResizeMode { get; set; }
    [Option('a', "anchor", Default = CropAnchor.Center, HelpText = "Specifies the horizontal and vertical anchor positions for auto cropping (Center, Top, Bottom, Left, Right) (pass multiple values separated by commas)")]
    public CropAnchor Anchor { get; set; }
    [JsonIgnore, Option("matte-color", HelpText = "(Default: Black) Specifies the color to use when padding or converting transparency to non-transparent formats (Use names or values: White; Brown; #FFFFA0; 255,255,160...)")] 
    public System.Drawing.Color BackColor { get; set; } = System.Drawing.Color.Black;
    public string MatteColor => BackColor.ToString()[6..];
    [Option("hybrid-mode", Default = HybridScaleMode.Off, HelpText = "Defines the mode that control speed vs. quality trade-offs for high-ratio scaling operations (off, favorquality, favorspeed, turbo)")]
    public HybridScaleMode ScaleMode { get; set; }
    [Option('t', "threshold", HelpText = "Minimum file size to resize (10kb, 1MB...)")]
    [JsonIgnore]
    public ByteSize? SizeThreshold { get; set; }
    public string? Threshold => SizeThreshold?.ToString();
    [Option('q', "quality", Default = 80, HelpText = "Defines perceptual output quality for lossy compressions (1..100)")]
    public int Quality { get; set; }
    [Option('c', "chroma", Default = ChromaSubsampleMode.Subsample420, HelpText = "Defines the Chroma Subsample mode to use for encoding")]
    public ChromaSubsampleMode ChromaSubsampleMode { get; set; }
    [Option('p', "progressive", Default = JpegProgressiveMode.None, HelpText = "Defines the JPEG Progressive mode to use for enconding")]
    public JpegProgressiveMode JpegProgressiveMode { get; set; }
    [Option("whatif", HelpText = "Runs in simulation mode (output isn't written to disk)")]
    public bool WhatIf { get; set; }
    public bool HasJob() => !string.IsNullOrWhiteSpace(Job);
    public override string ToString() => JsonSerializer.Serialize(this, new JsonSerializerOptions { 
        Converters = { new JsonStringEnumConverter(), }
    });
}

// ByteSize converting (needed for automatic command line parsing)
public class ByteSizeTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string s ? ByteSize.Parse(s) : base.ConvertFrom(context, culture, value);
    
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        value is ByteSize ? value.ToString() : base.ConvertTo(context, culture, value, destinationType);
}