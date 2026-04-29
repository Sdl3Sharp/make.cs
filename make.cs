#:property Version=0.0.7
#:package NuGet.Packaging@7.3.1
#:package System.CommandLine@2.0.7

using NuGet.Packaging;
using NuGet.Versioning;
using System.Collections.Immutable;
using System.CommandLine;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

// ===== Configuration =====
const string DefaultConfigFileName = "make.json",  DefaultProjectPath = "./src",             
             DefaultCacheFileName  = "cache.json", DefaultNugetSource = "https://api.nuget.org/v3/index.json",
             DefaultDocsPath       = "./docs";

static string getSourceFilePath([CallerFilePath] string path = "") => path;

// ===== CLI options and arguments =====
var projectOption                    = new Option<FileSystemInfo?>("--project")                        { Description = "Path to a .csproj file or a directory containing one. If a directory is given, the first .csproj inside it will be used.",                   Arity = ArgumentArity.ExactlyOne };
var configOption                     = new Option<string?>        ("--configuration")                  { Description = "Build configuration to use (e.g. Debug or Release).",                                                                                        Arity = ArgumentArity.ExactlyOne };
var defineOption                     = new Option<string[]?>      ("--define")                         { Description = "One or more preprocessor symbols to define (semicolon or comma separated).",                                                                 Arity = ArgumentArity.ZeroOrMore };
var noRestoreOption                  = new Option<bool?>          ("--no-restore")                     { Description = "Skip the restore phase when building or packing." };
var propertyOption                   = new Option<string[]?>      ("--property")                       { Description = "Additional MSBuild properties in the form name=value.",                                                                                      Arity = ArgumentArity.ZeroOrMore };
var verboseOption                    = new Option<bool?>          ("--verbose")                        { Description = "Enable verbose logging with detailed output." };
var noLogoOption                     = new Option<bool?>          ("--no-logo")                        { Description = "Suppress the startup logo." };
var logoFileOption                   = new Option<FileInfo?>      ("--logo-file")                      { Description = "Path to a text file containting the startup logo ASCII art.",                                                                                Arity = ArgumentArity.ExactlyOne };
var outputDirOption                  = new Option<string?>        ("--output-dir")                     { Description = "Directory where build or pack outputs will be placed.",                                                                                      Arity = ArgumentArity.ExactlyOne };
var cacheDirOption                   = new Option<string?>        ("--cache-dir")                      { Description = "Directory to store cached downloads (e.g. runtimes archives).",                                                                              Arity = ArgumentArity.ExactlyOne }; 
var tempDirOption                    = new Option<string?>        ("--temp-dir")                       { Description = "Temporary working directory used during packing.",                                                                                           Arity = ArgumentArity.ExactlyOne };
var runtimesVersionOption            = new Option<string?>        ("--runtimes-version")               { Description = "Version of the runtimes package to download and include in RID-specific packages.",                                                          Arity = ArgumentArity.ExactlyOne };
var runtimesUrlOption                = new Option<string?>        ("--runtimes-url")                   { Description = "URL or format string for the runtimes archive. Use '{0}' as a placeholder for the version.",                                                 Arity = ArgumentArity.ExactlyOne };
var runtimesLicenseSpdxOption        = new Option<string?>        ("--runtimes-license-spdx")          { Description = "SPDX license expression to apply to RID packages (e.g. MIT, Apache-2.0).",                                                                   Arity = ArgumentArity.ExactlyOne };
var runtimesLicenseFileUrlOption     = new Option<string?>        ("--runtimes-license-file-url")      { Description = "URL or format string to a license file to include in RID packages. If no SPDX is set, also used as PackageLicenseFile.",                     Arity = ArgumentArity.ExactlyOne };
var runtimesLicenseSpdxFileUrlOption = new Option<string?>        ("--runtimes-license-spdx-file-url") { Description = "URL or format string to a text file containing an SPDX identifier. Used as PackageLicenseExpression if --runtimes-license-spdx is not set.", Arity = ArgumentArity.ExactlyOne };
var forceRuntimesDownloadOption      = new Option<bool?>          ("--force-runtimes-download")        { Description = "Force re-download of runtimes archive even if a cached version exists." };
var targetsOption                    = new Option<string[]?>      ("--targets")                        { Description = "List of targets to pack. Use 'all' for all, 'core' for the main package, 'meta' for the meta package, or specify RIDs.",                     Arity = ArgumentArity.ZeroOrMore };
var strictOption                     = new Option<bool?>          ("--strict")                         { Description = "Fail if a requested RID has no native binary instead of warning." };
var noSymbolsOption                  = new Option<bool?>          ("--no-symbols")                     { Description = "Do not create a symbols package for the core project." };
var nugetSourceOption                = new Option<string?>        ("--nuget-source")                   { Description = $"NuGet feed URL. Defaults to '{DefaultNugetSource}'.",                                                                                       Arity = ArgumentArity.ExactlyOne };
var apiKeyOption                     = new Option<string>         ("--api-key")                        { Description = "API key for the NuGet feed.",                                                                                                                Arity = ArgumentArity.ExactlyOne,   Required = true };
var noPackOption                     = new Option<bool?>          ("--no-pack")                        { Description = "Do not 'pack' even if cache is stale." };
var failStaleOption                  = new Option<bool?>          ("--fail-stale")                     { Description = "Exit with error if cache is stale instead of packing." };
var docfxOption                      = new Option<FileSystemInfo?>("--docfx")                          { Description = "Path to a docfx.json file or a directory containing one. If a directory is given, the first docfx.json inside it will be used.",             Arity = ArgumentArity.ExactlyOne };
var docsApiOutputOption              = new Option<string?>        ("--docs-api-output")                { Description = "Override the API documentation output directory. This gets passed as the \"--output\" argument to \"docfx metadata\".",                     Arity = ArgumentArity.ExactlyOne };
var docsOutputOption                 = new Option<string?>        ("--docs-output")                    { Description = "Override the documentation output directory. This gets passed as the \"--output\" argument to \"docfx build\".",                            Arity = ArgumentArity.ExactlyOne };
var withoutMetadataOption            = new Option<bool?>          ("--without-metadata")               { Description = "Do not include metadata in the generated documentation (e.g. API reference). Doesn't run \"docfx metadata\" before \"docfx build\"." };
var withoutBuildOption               = new Option<bool?>          ("--without-build")                  { Description = "Do not build the documentation or generate HTML output. Doesn't run \"docfx build\" after \"docfx metadata\"." };
var buildBeforeDocsOption            = new Option<bool?>          ("--build-before-docs")              { Description = "Build the project before running DocFX (useful for binary-based documentation)." };
var requireDocFxMinVersionOption     = new Option<string?>        ("--require-docfx-min-version")      { Description = "Minimum required DocFX version (e.g. 2.75.0). Fails if the installed version is older.",                                                     Arity = ArgumentArity.ExactlyOne };
var nCoverHelpOption                 = new Option<bool?>          ("--ncover-help")                    { Description = "Show help for the ncover.cs tool (as if \"ncover.cs --help\" was run)." };

var configPathArgument = new Argument<FileSystemInfo?>("CONFIG_PATH")
{
    HelpName = "CONFIG_PATH",
    Description = $"Optional path to a configuration file or a directory containing one. If omitted, the tool looks for '{DefaultConfigFileName}' in the current directory.",
    Arity = ArgumentArity.ZeroOrOne
};

// Discover "ncover.cs" relative to this source file
var nCoverFile = new FileInfo(Path.Combine(Path.GetDirectoryName(getSourceFilePath())!, "ncover.cs", "ncover.cs"));

// ===== Root command =====
var rootCommand = new RootCommand($"Build and package tool for managed projects + native runtimes");

// ===== build command =====
var buildCommand = new Command("build", "Build the managed project")
{
    projectOption,  configOption,
    defineOption,   noRestoreOption,
    propertyOption, verboseOption,
    noLogoOption,   logoFileOption,
    configPathArgument
};
buildCommand.SetAction(GlobalSetupAsync(ProjectSetupAsync(HandleBuildAsync)));
rootCommand.Add(buildCommand);

// ===== clean command =====
var cleanCommand = new Command("clean", "Clean temp, cache, and output directories")
{
    projectOption,   configOption,
    noRestoreOption, propertyOption,
    outputDirOption, cacheDirOption,
    tempDirOption,   verboseOption,
    noLogoOption,    logoFileOption,
    configPathArgument
};
cleanCommand.SetAction(GlobalSetupAsync(ProjectSetupAsync(HandleCleanAsync)));
rootCommand.Add(cleanCommand);

// ===== pack command =====
var packCommand = new Command("pack", "Package NuGet artifacts")
{
    runtimesVersionOption,        runtimesUrlOption,
    forceRuntimesDownloadOption,  runtimesLicenseSpdxOption,
    runtimesLicenseFileUrlOption, runtimesLicenseSpdxFileUrlOption,
    targetsOption,                strictOption,
    projectOption,                configOption,
    defineOption,                 noSymbolsOption,
    noRestoreOption,              propertyOption,
    outputDirOption,              cacheDirOption,
    tempDirOption,                verboseOption,
    noLogoOption,                 logoFileOption,
    configPathArgument
};
packCommand.SetAction(GlobalSetupAsync(ProjectSetupAsync(HandlePackAsync)));
rootCommand.Add(packCommand);

// ===== push command =====
var pushCommand = new Command("push", "Push NuGet packages to a feed")
{
    nugetSourceOption,            apiKeyOption,
    noPackOption,                 failStaleOption,
    runtimesVersionOption,        runtimesUrlOption,
    forceRuntimesDownloadOption,  runtimesLicenseSpdxOption,
    runtimesLicenseFileUrlOption, runtimesLicenseSpdxFileUrlOption,
    targetsOption,                strictOption,
    projectOption,                configOption,
    defineOption,                 noSymbolsOption,
    noRestoreOption,              propertyOption,
    outputDirOption,              cacheDirOption,
    tempDirOption,                verboseOption,
    noLogoOption,                 logoFileOption,
    configPathArgument
};
pushCommand.SetAction(GlobalSetupAsync(ProjectSetupAsync(HandlePushAsync)));
rootCommand.Add(pushCommand);

// ===== docs command =====
var docsCommand = new Command("docs", "Build project documentation using DocFX")
{
    docfxOption,                  docsApiOutputOption,
    docsOutputOption,             withoutMetadataOption,
    withoutBuildOption,           buildBeforeDocsOption,
    requireDocFxMinVersionOption, projectOption,
    configOption,                 defineOption,
    noRestoreOption,              propertyOption,
    verboseOption,                noLogoOption,
    logoFileOption,
    configPathArgument
};
docsCommand.SetAction(GlobalSetupAsync(DocsSetupAsync(HandleDocsAsync)));
rootCommand.Add(docsCommand);

// ===== docs clean command =====
var docsCleanCommand = new Command("clean", "Clean documentation output directory")
{
    docfxOption,      docsApiOutputOption,
    docsOutputOption, verboseOption,
    noLogoOption,     logoFileOption,
    configPathArgument
};
docsCleanCommand.SetAction(GlobalSetupAsync(DocsSetupAsync(HandleDocsCleanAsync)));
docsCommand.Add(docsCleanCommand);

// ==== ncover command =====
Option<FileInfo?> nCoverExcludeFileOption;
Option<string[]?> nCoverExcludeOption;
Option<string?>   nCoverMinSeverityOption;
Option<bool?>     nCoverSlightAsWarnOption;
Option<bool?>     nCoverWarnAsErrorOption;
Option<FileInfo?> nCoverJsonOutputOption;
Option<bool?>     nCoverNoUnicodeOption;
Option<bool?>     nCoverNoAnsiOption;
Option<string?>   nCoverVerbosityOption;
Option<bool?>     nCoverPrettyOption;
Command nCoverCommand;
if (nCoverFile.Exists)
{
    nCoverExcludeFileOption  = new Option<FileInfo?>("--exclude-file", "-x")   { Description = "Path to a file containing a list of symbol names to exclude from the comparison. One symbol name per line.",                                                  Arity = ArgumentArity.ExactlyOne, HelpName = "EXCLUDE-FILE" };
    nCoverExcludeOption      = new Option<string[]?>("--exclude", "-X")        { Description = "Symbol names to exclude from the comparison. Can be specified multiple times.",                                                                               Arity = ArgumentArity.OneOrMore,  HelpName = "EXCLUDE",         AllowMultipleArgumentsPerToken = true };
    nCoverMinSeverityOption  = new Option<string?>  ("--min-severity", "-m")
    {
        Description = "Minimum severity level of the report. Reports with a severity level below this value will be ignored."
                    + " Must be one of: 'all', 'slight', 'warn', 'error', 'none', or an integer between 0 and 4. Defaults to 'all'.",
        Arity       = ArgumentArity.ExactlyOne,
        HelpName    = "MIN-SEVERITY"
    };
    nCoverSlightAsWarnOption = new Option<bool?>    ("--slight-as-warn", "-w") { Description = "Treat slight issues as warnings." };
    nCoverWarnAsErrorOption  = new Option<bool?>    ("--warn-as-error", "-e")  { Description = "Treat warnings as errors." };
    nCoverJsonOutputOption   = new Option<FileInfo?>("--json-output", "-j")    { Description = $"Path to a file where the report will be additionally saved in JSON format. The JSON report will respect the {nCoverMinSeverityOption.Name} option as well.", Arity = ArgumentArity.ExactlyOne, HelpName = "JSON-OUTPUT-FILE" };
    nCoverNoUnicodeOption    = new Option<bool?>    ("--no-unicode", "-u")     { Description = $"Disable Unicode output for the standard output (e.g., use ASCII symbols instead of Unicode symbols)." };
    nCoverNoAnsiOption       = new Option<bool?>    ("--no-ansi", "-a")        { Description = "Disable ANSI escape codes in the standard output (e.g., disables colored output)." };
    nCoverVerbosityOption    = new Option<string?>  ("--verbosity", "-v")
    {
        Description = "Verbosity level of the output."
                    + $" Must be one of: 'none', 'default', 'verbose', 'stats', or 'json', or an integer between -1 and 3. Defaults to 'stats'."
                    + $" Specifying 'none' will suppress any output to the standard output. This can be useful in conjunction with the {nCoverJsonOutputOption.Name} option."
                    + $" Specifying 'stats' will output only summary statistics about the comparison, and no detailed report entries."
                    + $" Specifying 'json' will output the report in JSON format to the standard output instead. That's the same output as generated by the {nCoverJsonOutputOption.Name} option, but other than that independent of that option, which means both can specified at the same time.",
        Arity       = ArgumentArity.ExactlyOne,
        HelpName    = "VERBOSITY"
    };
    nCoverPrettyOption       = new Option<bool?>    ("--pretty")               { Description = $"Only used when the {nCoverJsonOutputOption.Name} option is specified or when {nCoverVerbosityOption.Name} is set to 'json'. When enabled, the JSON output will be pretty-printed with indentation and newlines." };

    nCoverCommand = new Command("ncover", "Run ncover.cs on the managed project and compare it against the native runtimes download")
    {
        projectOption,               configOption,
        defineOption,                noRestoreOption,
        propertyOption,              verboseOption,
        noLogoOption,                logoFileOption,
        runtimesVersionOption,       runtimesUrlOption,
        forceRuntimesDownloadOption, nCoverExcludeFileOption,
        nCoverExcludeOption,         nCoverMinSeverityOption,
        nCoverSlightAsWarnOption,    nCoverWarnAsErrorOption,
        nCoverJsonOutputOption,      nCoverNoUnicodeOption,
        nCoverNoAnsiOption,          nCoverVerbosityOption,
        nCoverPrettyOption,          nCoverHelpOption,
        configPathArgument
    };
    nCoverCommand.SetAction(GlobalSetupAsync(ProjectSetupAsync(HandleNCoverAsync), checkNCoverHelp: true));
    rootCommand.Add(nCoverCommand);
}

// ===== Invoke =====
return await rootCommand.Parse(args).InvokeAsync();

// ===== Setup wrappers =====
Func<ParseResult, CancellationToken, Task<int>> GlobalSetupAsync(HandlerAsync continuationAsync, bool checkNCoverHelp = false) => async (parserResult, cancellationToken) =>
{
    const string noLogoPropertyName  = "noLogo", logoFilePropertyName = "logoFile";

    var noRestore = parserResult.GetValue(noRestoreOption) ?? false;

    if (checkNCoverHelp && parserResult.GetValue(nCoverHelpOption) is true)
    { return await RunDotnetAsync([ "run", nCoverFile.FullName ], [ "--", "--help" ], config: null, noRestore: noRestore, noLogo: false /* dontnet run doesn't actuall accept a "--no-logo" option */ , properties: [], @out: null, @error: null, cancellationToken); }

    var logger = new Logger(parserResult.InvocationConfiguration.Output, parserResult.InvocationConfiguration.Error, IsVerbose: parserResult.GetValue(verboseOption) ?? false);

    FileInfo? configFile;
    switch (parserResult.GetValue(configPathArgument))
    {
        case FileInfo { Exists: true } fileInfo: { configFile = fileInfo; break; }
        case DirectoryInfo { Exists: true, FullName: var fullName }:
        {
            if (Path.Combine(fullName, DefaultConfigFileName) is var candidate && File.Exists(candidate)) { configFile = new(candidate); break; }
            return await logger.FailAsync($"No configuration file named '{DefaultConfigFileName}' found in directory '{fullName}'.", cancellationToken);
        }
        case { Exists: false, FullName: var fullName }: { return await logger.FailAsync($"The specified configuration path '{fullName}' does not exist.", cancellationToken); }
        default:
        { 
            if (Path.Combine(Environment.CurrentDirectory, DefaultConfigFileName) is var candidate && File.Exists(candidate)) { configFile = new(candidate); break; }
            configFile = null; break;
        }
    }

    string? oldCwd = null;
    JsonDocument? jsonDocument = null;
    if (configFile is not null)
    {
        oldCwd = Directory.GetCurrentDirectory();
        var cwd = configFile.Directory!.FullName;
        await logger.OutputVerboseAsync(() => $"Configuration file found at '{configFile.FullName}'. Switching current working directory to '{cwd}'...", cancellationToken);
        Directory.SetCurrentDirectory(cwd);

        try
        {
            await using var stream = configFile.OpenRead();
            jsonDocument = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException) { return await logger.FailAsync($"The configuration file '{configFile.FullName}' contains invalid JSON.", cancellationToken); }
        catch (IOException e) { return await logger.FailAsync($"Failed to read configuration file '{configFile.FullName}': [{e.GetType().Name}]: {e.Message}", cancellationToken); }
    }

    try
    {        
        var options = new Options(parserResult, jsonDocument);

        var noLogo = options.GetBoolean(noLogoOption, noLogoPropertyName, false);
        var logoFile = options.GetFileSystemInfo(logoFileOption, logoFilePropertyName);

        if (!noLogo && logoFile is not null)
        {
            if (logoFile.Exists)
            {
                // Print the logo
                await using var logoStream = logoFile.OpenRead();
                using var reader = new StreamReader(logoStream);

                while (await reader.ReadLineAsync(cancellationToken) is var line && line is not null)
                {
                    int width;
                    try { width = Console.WindowWidth; }
                    catch { width = -1; }

                    await logger.Out.WriteLineAsync(line.TruncateToMaxLength(width).AsMemory(), cancellationToken);
                }
                await logger.Out.WriteLineAsync();
            }
            else { await logger.ErrorAsync($"Couldn't find the specified logo file at '{logoFile.FullName}'.", cancellationToken); }
        }

        return await continuationAsync(logger, options, noRestore, noLogo, cancellationToken);
    }    
    finally
    {
        jsonDocument?.Dispose();        
        if (oldCwd is not null) { Directory.SetCurrentDirectory(oldCwd); }
    }
};

string GetTempDir(Options options) => options.GetString(tempDirOption, "tempDir", "./temp");

HandlerAsync ProjectSetupAsync(ProjectHandlerAsync continuationAsync) => async (logger, options, noRestore, noLogo, cancellationToken) =>
{
    const string projectPropertyName = "project";

    FileInfo projectFile;
    switch (options.GetFileSystemInfo(projectOption, projectPropertyName))
    {
        case FileInfo { Exists: true } fileInfo: { projectFile = fileInfo; break; }
        case DirectoryInfo { Exists: true, FullName: var fullName } dirInfo:
        {
            if (dirInfo.EnumerateFiles("*.csproj").FirstOrDefault() is { } fileInfo) { projectFile = fileInfo; break; }
            return await logger.FailAsync($"No project file (*.csproj) found in directory '{fullName}'.", cancellationToken);
        }
        case { Exists: false, FullName: var fullName }: { return await logger.FailAsync($"The specified project path '{fullName}' does not exist.", cancellationToken); }
        default:
        {
            if (Path.Combine(Environment.CurrentDirectory, DefaultProjectPath) is var projectPath && Directory.Exists(projectPath) && new DirectoryInfo(projectPath).EnumerateFiles("*.csproj").FirstOrDefault() is { } fileInfo) { projectFile = fileInfo; break; }
            return await logger.FailAsync($"No project file could be resolved. Provide {projectOption.Name}, set '{projectPropertyName}' in the config, or place a .csproj in '{DefaultProjectPath}'.", cancellationToken);
        }
    }    
        
    var tempDirPath = GetTempDir(options);

    await using var httpClient = new Shared<HttpClient>(() => new());
    await using var tempDirectory = new Shared<TempDirectory>(() => new(tempDirPath));
    return await continuationAsync(logger, options, projectFile, noRestore, noLogo, httpClient, tempDirectory, cancellationToken);
};

HandlerAsync DocsSetupAsync(DocsHandlerAsync continuationAsync) => async (logger, options, noRestore, noLogo, cancellationToken) =>
{
    const string docfxPropertyName = "docfx";

    FileInfo docsFile;
    switch (options.GetFileSystemInfo(docfxOption, docfxPropertyName))
    {
        case FileInfo { Exists: true } fileInfo: { docsFile = fileInfo; break; }
        case DirectoryInfo { Exists: true, FullName: var fullName } dirInfo:
        {
            if (dirInfo.EnumerateFiles("docfx.json").FirstOrDefault() is { } fileInfo) { docsFile = fileInfo; break; }
            return await logger.FailAsync($"No 'docfx.json' found in directory '{fullName}'.", cancellationToken);
        }
        case { Exists: false, FullName: var fullName }: { return await logger.FailAsync($"The specified docfx path '{fullName}' does not exist.", cancellationToken); }
        default:
        {
            if (Path.Combine(Environment.CurrentDirectory, DefaultDocsPath) is var docsPath && Directory.Exists(docsPath) && new DirectoryInfo(docsPath).EnumerateFiles("docfx.json").FirstOrDefault() is { } fileInfo) { docsFile = fileInfo; break; }
            return await logger.FailAsync($"No 'docfx.json' could be resolved. Provide {docfxOption.Name}, set '{docfxPropertyName}' in the config, or place a 'docfx.json' in '{DefaultDocsPath}'.", cancellationToken);
        }
    }

    return await continuationAsync(logger, options, docsFile, noRestore, noLogo, cancellationToken);
};

// ===== Handlers =====
async Task<int> HandleBuildAsync(Logger logger, Options options, FileInfo projectFile, bool noRestore, bool noLogo, Shared<HttpClient> httpClient, Shared<TempDirectory> tempDirectory, CancellationToken cancellationToken)
{
    var config     = options.ParseResult.GetValue(configOption)    ?? "Debug";
    var defines    = options.ParseResult.GetValue(defineOption)    ?? [];
    var properties = options.ParseResult.GetValue(propertyOption)  ?? [];

    await logger.OutputVerboseAsync(() => $"Configuration: {config}, NoRestore: {noRestore}", cancellationToken);
    await logger.OutputAsync($"Building project ({config})...", cancellationToken);
    await logger.OutputVerboseAsync(() => $"Project file: '{projectFile.FullName}'", cancellationToken);

    // Run dotnet build
    await logger.OutputDotnetCliAsync([ "build", projectFile.FullName ], [
        defines.Length is > 0 ? $"/p:DefineConstants={string.Join(";", defines)}" : null
    ], config, noRestore, noLogo, properties, cancellationToken);

    var exit = await RunDotnetAsync([ "build", projectFile.FullName ], [        
        defines.Length is > 0 ? $"/p:DefineConstants={string.Join(";", defines)}" : null
    ], config, noRestore, noLogo, properties, @out: null, @error: null, cancellationToken);

    await logger.OutputDotnetFinishedAsync([ "build", projectFile.FullName ], exit, cancellationToken);

    return exit;
}

string GetOutputDir(Options options) => options.GetString(outputDirOption, "outputDir", "./output");
string GetCacheDir (Options options) => options.GetString(cacheDirOption,  "cacheDir",  "./cache");

async Task<int> HandleCleanAsync(Logger logger, Options options, FileInfo projectFile, bool noRestore, bool noLogo, Shared<HttpClient> httpClient, Shared<TempDirectory> tempDirectory, CancellationToken cancellationToken)
{
    var config     = options.ParseResult.GetValue(configOption);
    var properties = options.ParseResult.GetValue(propertyOption) ?? [];
    var outputDir  = GetOutputDir(options);
    var cacheDir   = GetCacheDir(options);
    var tempDir    = GetTempDir(options);
    
    await logger.OutputAsync($"Cleaning...", cancellationToken);
    await logger.OutputVerboseAsync(() => $"Project file: '{projectFile.FullName}'", cancellationToken);

    // Run dotnet clean
    await logger.OutputDotnetCliAsync([ "clean", projectFile.FullName ], [], config, noRestore, noLogo, properties, cancellationToken);

    var exit = await RunDotnetAsync([ "clean", projectFile.FullName ], [], config, noRestore, noLogo, properties, @out: null, @error: null, cancellationToken);

    await logger.OutputDotnetFinishedAsync([ "clean", projectFile.FullName ], exit, cancellationToken);

    // Remove our own output/cache/temp dirs
    exit = await DeleteDirectoryAsync(outputDir, exit, logger, cancellationToken);
    exit = await DeleteDirectoryAsync(cacheDir, exit, logger, cancellationToken);
    exit = await DeleteDirectoryAsync(tempDir, exit, logger, cancellationToken);

    return exit;
}

#pragma warning disable CS0162 // Why is this still a thing for local constants which are part of a top-level statement program? They're most certainly not 'unreachable' and even more certainly not 'code'
const string RuntimesVersionPropertyName = "runtimesVersion",
             RuntimesUrlPropertyName     = "runtimesUrl";
#pragma warning restore CS0162
string?  GetRuntimesVersion(Options options) => options.GetString(runtimesVersionOption, RuntimesVersionPropertyName);
string?  GetRuntimesUrl    (Options options) => options.GetString(runtimesUrlOption,     RuntimesUrlPropertyName);

async Task<int> HandlePackAsync(Logger logger, Options options, FileInfo projectFile, bool noRestore, bool noLogo, Shared<HttpClient> httpClient, Shared<TempDirectory> tempDirectory, CancellationToken cancellationToken)
{
    var runtimesVersion            = GetRuntimesVersion(options);
    var runtimesUrl                = GetRuntimesUrl(options);
    var targets                    = options.ParseResult.GetValue(targetsOption)   ?? [];
    var strict                     = options.ParseResult.GetValue(strictOption)    ?? false;
    var config                     = options.ParseResult.GetValue(configOption)    ?? "Release";
    var defines                    = options.ParseResult.GetValue(defineOption)    ?? [];
    var noSymbols                  = options.ParseResult.GetValue(noSymbolsOption) ?? false;
    var properties                 = options.ParseResult.GetValue(propertyOption)  ?? [];
    var outputDir                  = GetOutputDir(options);
    var cacheDir                   = GetCacheDir(options);

    Directory.CreateDirectory(outputDir);
    Directory.CreateDirectory(cacheDir);

    var tempDir = (await tempDirectory.GetValueAsync(cancellationToken)).Info.FullName;
    var cacheFile = new FileInfo(Path.Combine(cacheDir, DefaultCacheFileName));
    var outputDirDir = new DirectoryInfo(outputDir);

    if (cacheFile.Exists)
    {
        try { cacheFile.Delete(); }
        catch (Exception e) { return await logger.FailAsync($"Failed to delete '{cacheFile.FullName}': [{e.GetType().Name}]: {e.Message}", cancellationToken); }
    }

    foreach (var nupkg in outputDirDir.EnumerateFiles("*.nupkg").Concat(outputDirDir.EnumerateFiles("*.snupkg")))
    {
        await logger.OutputVerboseAsync(() => $"Deleting '{nupkg.FullName}'.", cancellationToken);
        try { nupkg.Delete(); }
        catch (Exception e) { return await logger.FailAsync($"Failed to delete '{nupkg.FullName}': [{e.GetType().Name}]: {e.Message}", cancellationToken); }
    }

    foreach (ref var target in targets.AsSpan()) { target = target.NormalizeLower(); }

    var packAll = targets.Length is 0 || targets.Contains("all");

    await logger.OutputVerboseAsync(() => $"Targets: {(packAll ? "All" : $"{string.Join(", ", targets)} ({targets.Length})")}", cancellationToken);
    await logger.OutputVerboseAsync(() =>  $"Configuration: {config}, NoSymbols: {noSymbols}, NoRestore: {noRestore}, Strict: {strict}", cancellationToken);

    List<(string Flavor, FileInfo File, string Id, string Version)> packed = [];

    var packCore = packAll || targets.Contains("core");
    if (packCore)
    {
        await logger.OutputAsync($"Packing Core package...", cancellationToken);
        await logger.OutputVerboseAsync(() => $"Project path: '{projectFile.FullName}'", cancellationToken);

        // Run dotnet pack
        await logger.OutputDotnetCliAsync([ "pack", projectFile.FullName ], [
            "-o", outputDir,
            defines.Length is > 0 ? $"/p:DefineConstants={string.Join(";", defines)}" : null,
            !noSymbols ? "-p:IncludeSymbols=true" : null, !noSymbols ? "-p:SymbolPackageFormat=snupkg" : null,
        ], config, noRestore, noLogo, properties, cancellationToken);

        var exit = await RunDotnetAsync([ "pack", projectFile.FullName ], [
            "-o", outputDir,
            defines.Length is > 0 ? $"/p:DefineConstants={string.Join(";", defines)}" : null,
            !noSymbols ? "-p:IncludeSymbols=true" : null, !noSymbols ? "-p:SymbolPackageFormat=snupkg" : null,
        ], config, noRestore, noLogo, properties, @out: null, @error: null, cancellationToken);

        await logger.OutputDotnetFinishedAsync([ "pack", projectFile.FullName ], exit, cancellationToken);

        if (exit is not 0) { return exit; }

        if (outputDirDir.EnumerateFiles("*.nupkg").Except(packed.Select(static p => p.File), (IEqualityComparer<FileInfo>)FileSystemInfoEqualityComparer.Instance).FirstOrDefault() is var file && file is null)
        { return await logger.FailAsync("Failed to find newly created nupkg file.", cancellationToken); }
        if (await file.GetNuGetPackageIdentityAsync(cancellationToken) is not var (id, version)) { return await logger.FailAsync($"Failed to get the identity of the newly created nupkg file '{file.FullName}'.", cancellationToken); }

        packed.Add(("core", file, id, version));

        await logger.OutputVerboseAsync(() => $"Core package successfully packed as '{file.FullName}'.", cancellationToken);
    }

    var packAnyRid = packAll || targets.Any(static t => t is not ("core" or "meta"));
    if (packAnyRid)
    {
        if (!(await DownloadRuntimesAsync(retrieveLicenseInfo: true, logger, options, httpClient, tempDirectory, cancellationToken)).TryGetValueOrElseError(out var runtimes, out var exit)) { return exit; }
        var (runtimesPath, availableRids, runtimesLicenseSpdx, runtimesLicensePath) = runtimes;

        await logger.OutputVerboseAsync(() => $"Available RIDs: {(availableRids.Length is not > 0 ? "None" : $"{string.Join(", ", availableRids)} ({availableRids.Length})")}", cancellationToken);

        var ridsToPack = packAll
            ? availableRids
            : [.. availableRids.Where(r => targets.Contains(r))];

        foreach (var target in targets.Where(static t => t is not ("core" or "meta")))
        {
            if (!availableRids.Contains(target))
            {
                var msg = $"Requested RID {target} not found in native runtime binaries.";
                if (strict) { return await logger.FailAsync(msg, cancellationToken); }
                else { await logger.ErrorAsync(msg, cancellationToken); }
            }
        }

        foreach (var rid in ridsToPack)
        {
            var nativePath = Path.Combine(runtimesPath, rid, "native");
            var nativeFile = Directory.Exists(nativePath)
                ? Directory.GetFiles(nativePath).FirstOrDefault()
                : null;

            if (nativeFile is null)
            {
                var msg = $"Missing native binary for {rid}";
                if (strict) { return await logger.FailAsync(msg, cancellationToken); }
                else { await logger.ErrorAsync(msg, cancellationToken); }
                await logger.ErrorVerboseAsync(() => $"Native binary path checked: '{nativePath}'", cancellationToken);

                continue;
            }

            var ridProjPath = Path.Combine(tempDir, $"{rid}.csproj");
            await File.WriteAllTextAsync(ridProjPath,
                $"""
                <!-- Auto-generated by build.cs. Do not edit manually. -->
                <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                        <IsPackable>true</IsPackable>
                        <IncludeBuildOutput>false</IncludeBuildOutput>
                        <NoBuild>true</NoBuild>
                        <RestoreAdditionalProjectSources>{Path.GetFullPath(outputDir).Replace("\\", "/")}</RestoreAdditionalProjectSources>
                        <!-- Make flavor: rid -->
                        <!-- use Condition="'$(MakeFlavor)' == 'rid'" in your "Directory.Build.targets" -->
                        <MakeFlavor>rid</MakeFlavor>
                        <!-- Make flavor rid: {rid} -->
                        <!-- use Condition="'$(MakeFlavorRid)' == '{rid}'" in your "Directory.Build.targets" -->
                        <MakeFlavorRid>{rid}</MakeFlavorRid>
                        {(runtimesLicenseSpdx is not null ? $"<PackageLicenseExpression>{runtimesLicenseSpdx}</PackageLicenseExpression>" : string.Empty)}
                        {(runtimesLicenseSpdx is null && runtimesLicensePath is not null ? $"<PackageLicenseFile>{Path.GetFileName(runtimesLicensePath)}</PackageLicenseFile>" : string.Empty)}
                    </PropertyGroup>
                    <ItemGroup>
                        {(packCore ? $"<PackageReference Include=\"{packed[0].Id}\" Version=\"{packed[0].Version}\" />" : string.Empty)}
                        <None Include="{Path.GetFullPath(nativeFile).Replace("\\", "/")}" Pack="true" PackagePath="runtimes/{rid}/native" />
                        {(runtimesLicensePath is not null ? $"<None Include=\"{Path.GetFullPath(runtimesLicensePath).Replace("\\", "/")}\" Pack=\"true\" PackagePath=\"\"/>" : string.Empty)}
                    </ItemGroup>
                </Project>
                """,
                cancellationToken
            );

            await logger.OutputAsync($"Packing {rid} package...", cancellationToken);

            // Run dotnet pack
            await logger.OutputDotnetCliAsync([ "pack", ridProjPath ], [
                "-o", outputDir,
            ], config, noRestore, noLogo, properties, cancellationToken);

            exit = await RunDotnetAsync([ "pack", ridProjPath ], [
                "-o", outputDir,
            ], config, noRestore, noLogo, properties, @out: null, @error: null, cancellationToken);

            await logger.OutputDotnetFinishedAsync([ "pack", ridProjPath ], exit, cancellationToken);

            if (exit is not 0) { return exit; }

            if (outputDirDir.EnumerateFiles("*.nupkg").Except(packed.Select(static p => p.File), (IEqualityComparer<FileInfo>)FileSystemInfoEqualityComparer.Instance).FirstOrDefault() is var file && file is null)
            { return await logger.FailAsync("Failed to find newly created nupkg file.", cancellationToken); }

            if (await file.GetNuGetPackageIdentityAsync(cancellationToken) is not var (id, version)) { return await logger.FailAsync($"Failed to get the identity of the newly created nupkg file '{file.FullName}'.", cancellationToken); }
            packed.Add((rid, file, id, version));

            await logger.OutputVerboseAsync(() => $"RID package {rid} successfully packed as '{file.FullName}'.", cancellationToken);
        }
    }

    var packMeta = packAll || targets.Contains("meta");
    if (packMeta)
    {
        await logger.OutputAsync("Packing Meta package...", cancellationToken);

        string[] deps;
        switch (packed.Count)
        {
            case <= 0: deps = []; await logger.ErrorAsync("Warning: Meta package will have no dependencies.", cancellationToken); break;
            case 1 when packCore: deps = [ $"<PackageReference Include=\"{packed[0].Id}\" Version=\"{packed[0].Version}\" />" ]; break;
            default: deps = [ ..packed.Skip(packCore ? 1 : 0).Select(static p => $"<PackageReference Include=\"{p.Id}\" Version=\"{p.Version}\" />") ]; break;
        }

        var metaProjPath = Path.Combine(tempDir, "meta.csproj");
        await File.WriteAllTextAsync(metaProjPath,
            $"""
            <!-- Auto-generated by build.cs. Do not edit manually. -->
            <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                    <IsPackable>true</IsPackable>
                    <IncludeBuildOutput>false</IncludeBuildOutput>
                    <NoBuild>true</NoBuild>
                    <RestoreAdditionalProjectSources>{Path.GetFullPath(outputDir).Replace("\\", "/")}</RestoreAdditionalProjectSources>
                    <!-- Make flavor: meta -->
                    <!-- use Condition="'$(MakeFlavor)' == 'meta'" in your "Directory.Build.targets" -->
                    <MakeFlavor>meta</MakeFlavor>
                </PropertyGroup>
                <ItemGroup>
                    {string.Join("\n        ", deps)}
                </ItemGroup>
            </Project>
            """,
            cancellationToken
        );

        // Run dotnet pack
        await logger.OutputDotnetCliAsync([ "pack", metaProjPath ], [
            "-o", outputDir,
        ], config, noRestore, noLogo, properties, cancellationToken);

        var exit = await RunDotnetAsync([ "pack", metaProjPath ], [
            "-o", outputDir,
        ], config, noRestore, noLogo, properties, @out: null, @error: null, cancellationToken);

        await logger.OutputDotnetFinishedAsync([ "pack", metaProjPath ], exit, cancellationToken);

        if (exit is not 0) return exit;

        if (outputDirDir.EnumerateFiles("*.nupkg").Except(packed.Select(static p => p.File), (IEqualityComparer<FileInfo>)FileSystemInfoEqualityComparer.Instance).FirstOrDefault() is var file && file is null)
        { return await logger.FailAsync("Failed to find newly created nupkg file.", cancellationToken); }
        if (await file.GetNuGetPackageIdentityAsync(cancellationToken) is not var (id, version)) { return await logger.FailAsync($"Failed to get the identity of the newly created nupkg file '{file.FullName}'.", cancellationToken); }
        packed.Add(("meta", file, id, version));

        await logger.OutputVerboseAsync(() => $"Meta package successfully packed as '{file.FullName}'.", cancellationToken);
    }

    var cache = new CacheData(
        Version: Assembly.GetAssembly(typeof(Program))!.GetName().Version!,
        Inputs: new(
            RuntimesVersion: runtimesVersion ?? string.Empty,
            RuntimesUrl:     runtimesUrl ?? string.Empty,
            Config:          config,
            NoSymbols:       noSymbols,
            Defines:         [..defines],
            Properties:      [..properties]
        ),
        Targets: packed.ToImmutableSortedDictionary(keySelector: static p => p.Flavor, elementSelector: static p => p.File.FullName)
    );

    try
    {
        await using var cacheFileStream = cacheFile.OpenWrite();            
        await JsonSerializer.SerializeAsync(cacheFileStream, cache, CacheDataSerializerContext.Default.CacheData, cancellationToken);
    }
    catch (Exception e) { return await logger.FailAsync($"Failed to serialize cache file to '{cacheFile.FullName}': [{e.GetType().Name}]: {e.Message}", cancellationToken); }

    await logger.OutputAsync("Packaging complete.", cancellationToken);
    await logger.OutputVerboseAsync(() => $"Packed: {string.Join(", ", packed.Select(static p => $"{p.File.Name} [{p.Flavor}]"))} ({packed.Count}), Output location: {Path.GetFullPath(outputDir)}", cancellationToken);

    return 0;
}

async Task<int> HandlePushAsync(Logger logger, Options options, FileInfo projectFile, bool noRestore, bool noLogo, Shared<HttpClient> httpClient, Shared<TempDirectory> tempDirectory, CancellationToken cancellationToken)
{
    const int maxRetriesWithPack = 1;

    var nugetSource     = options.GetString(nugetSourceOption, "nugetSource", DefaultNugetSource);
    var apiKey          = options.ParseResult.GetRequiredValue(apiKeyOption);
    var noPack          = options.ParseResult.GetValue(noPackOption)          ?? false;
    var failStale       = options.ParseResult.GetValue(failStaleOption)       ?? false;
    var runtimesVersion = GetRuntimesVersion(options);
    var runtimesUrl     = GetRuntimesUrl(options);
    var targets         = options.ParseResult.GetValue(targetsOption)         ?? [];
    var config          = options.ParseResult.GetValue(configOption);
    var defines         = options.ParseResult.GetValue(defineOption);
    var noSymbols       = options.ParseResult.GetValue(noSymbolsOption);
    var properties      = options.ParseResult.GetValue(propertyOption);
    var outputDir       = GetOutputDir(options);
    var cacheDir        = GetCacheDir(options);

    await logger.OutputVerboseAsync(() => $"NuGet source: {nugetSource}", cancellationToken);
    await logger.OutputVerboseAsync(() => $"Targets: {(targets.Length is 0 || targets.Contains("all") ? "All" : $"{string.Join(", ", targets)} ({targets.Length})")}", cancellationToken);
    await logger.OutputVerboseAsync(() => $"NoPack: {noPack}, NoSymbols: {noSymbols switch { bool value => $"{value}", _ => "depends on cache" }}, FailStale: {failStale}", cancellationToken);    


    if (noPack && failStale) { return await logger.FailAsync($"Options '{noPackOption.Name}' and '{failStaleOption.Name}' cannot be used together.", cancellationToken); }       

    foreach(ref var target in targets.AsSpan()) { target = target.NormalizeLower(); }

    int exit;
    ImmutableSortedDictionary<string, string>? localTargets;
    bool?                                      localNoSymbols;

    var retry = 0;
    CheckCache:
    {
        localTargets = null;
        localNoSymbols = noSymbols;

        // check the cached inputs-targets-file exist
        var cacheFilePath = Path.Combine(cacheDir, DefaultCacheFileName);
        if (!File.Exists(cacheFilePath))
        {            
            await logger.ErrorAsync("Cache file not found - treating cache as stale.", cancellationToken);
            goto CacheStale;
        }

        CacheData cache;
        try
        {
            await using var cacheFileStream = File.OpenRead(cacheFilePath);
            cache = await JsonSerializer.DeserializeAsync(cacheFileStream, CacheDataSerializerContext.Default.CacheData, cancellationToken);
        }
        catch
        {
            await logger.ErrorAsync("Cache file invalid or corrupt - treating cache as stale.", cancellationToken);
            goto CacheStale;
        }

        var localCache = new CacheData(
            Version: Assembly.GetAssembly(typeof(Program))!.GetName().Version!,
            Inputs:  new(
                RuntimesVersion: runtimesVersion ?? cache.Inputs.RuntimesVersion,
                RuntimesUrl:     runtimesUrl     ?? cache.Inputs.RuntimesUrl,
                Config:          config          ?? cache.Inputs.Config,
                NoSymbols:       localNoSymbols ??= cache.Inputs.NoSymbols,
                Defines:         [..defines    ?? [..cache.Inputs.Defines]],
                Properties:      [..properties ?? [..cache.Inputs.Properties]]
            ),
            Targets: localTargets = (targets is [] || targets.Contains("all") ? targets.Where(target => target is not "all").Concat(cache.Targets.Keys).Distinct() : targets)
                .ToImmutableSortedDictionary(
                    keySelector: static rid => rid,
                    elementSelector: rid => cache.Targets.TryGetValue(rid, out var nupkg) ? Path.GetFullPath(nupkg) : string.Empty
                )
        );

        // check if the cache is up-to-date
        // the way it's done here is highly ineffecient, but it helps with two things:
        // - we don't have to deal with implementing a custom equality comparison for 'CacheData' and 'CacheData.InputsData' just because they happen to have collection types as their members
        // - even though we're using a 'Version' member to differentiate them, using the same JsonSerializerContext for both avoids having versioning issues
        var cacheJson      = JsonSerializer.Serialize(cache,      CacheDataSerializerContext.Default.CacheData);
        var localCacheJson = JsonSerializer.Serialize(localCache, CacheDataSerializerContext.Default.CacheData);
        if (!string.Equals(cacheJson, localCacheJson, StringComparison.Ordinal))
        {
            await logger.OutputVerboseAsync(() => "Current inputs differ from cached inputs - marking cache as stale.", cancellationToken);
            goto CacheStale;
        }

        // check expected packages
        string? coreNupkg = null;
        foreach (var (rid, nupkg) in localTargets)
        {
            await logger.OutputVerboseAsync(() => $"Checking package file: {nupkg}", cancellationToken);
            if (string.IsNullOrWhiteSpace(nupkg) || !File.Exists(nupkg))
            {
                await logger.ErrorAsync($"Expected package '{nupkg}' not found.", cancellationToken);
                goto CacheStale;
            }
            if (rid is "core") { coreNupkg = nupkg; }
        }

        // check symbols package
        if (!string.IsNullOrWhiteSpace(coreNupkg) && !localCache.Inputs.NoSymbols)
        {
            var snupkg = Path.ChangeExtension(coreNupkg, ".snupkg");
            await logger.OutputVerboseAsync(() => $"Checking symbols package file: {snupkg}", cancellationToken);
            if (!File.Exists(snupkg))
            {
                await logger.ErrorAsync($"Expected symbols package '{snupkg}' not found.", cancellationToken);
                goto CacheStale;
            }
        }

        // get most recent source file time using 'dotnet watch --list'
        var newestWatchedFilesWriteTime = DateTime.MinValue;
        exit = await RunProcessAsync("dotnet", [ "watch", "--list", "--project", projectFile.FullName ],
            async (procOut, cancellationToken) =>
            {
                var line = await procOut.ReadLineAsync(cancellationToken);
                if (line is null) { await Task.Yield(); return false; }
                if (!string.IsNullOrWhiteSpace(line) && File.Exists(line)) { newestWatchedFilesWriteTime = newestWatchedFilesWriteTime.Max(File.GetLastWriteTimeUtc(Path.GetFullPath(line))); }
                return true;
            },
            errorReaderAsync: null,
            cancellationToken
        );
        if (exit is not 0) { return await logger.FailAsync($"'dotnet watch --list' failed with exit code {exit}.", errorCode: exit, cancellationToken); }

        // get oldest package file time
        var oldestPackagesWriteTime = DateTime.MaxValue;
        foreach (var (_, nupkg) in localCache.Targets) { oldestPackagesWriteTime = oldestPackagesWriteTime.Min(File.GetLastWriteTimeUtc(nupkg)); }
        if (!string.IsNullOrWhiteSpace(coreNupkg) && !localCache.Inputs.NoSymbols) { oldestPackagesWriteTime = oldestPackagesWriteTime.Min(File.GetLastWriteTimeUtc(Path.ChangeExtension(coreNupkg, ".snupkg"))); }

        // check and compare file times
        if (newestWatchedFilesWriteTime > oldestPackagesWriteTime)
        {
            await logger.OutputVerboseAsync(() => $"Newest input ({newestWatchedFilesWriteTime:o}) > oldest package ({oldestPackagesWriteTime:o}) - marking cache as stale.", cancellationToken);
            goto CacheStale;
        }

        // we did what we could to ensure that the cache is up-to-date and we can skip retrying with a 'pack'
        goto CacheOkay;
    }

    CacheStale:
    {
        if (failStale) { return await logger.FailAsync($"Cache is stale and '{failStaleOption.Name}' was specified.", cancellationToken); }

        if (noPack)
        {
            await logger.OutputVerboseAsync(() => $"Cache is stale; proceeding without repacking due to '{noPackOption.Name}'.", cancellationToken);
            goto CacheOkay;
        }               

        if (retry++ >= maxRetriesWithPack) { return await logger.FailAsync("Cache is stale and repack attempt limit reached.", cancellationToken); }

        await logger.OutputVerboseAsync(() => $"Cache is stale - running '{packCommand.Name}' before push.", cancellationToken);
        exit = await HandlePackAsync(logger, options, projectFile, noRestore, noLogo, httpClient, tempDirectory, cancellationToken);
        if (exit is not 0) { return exit; }

        goto CheckCache;
    }

    CacheOkay:
    {
        var pushSymbols = !(localNoSymbols ?? false);

        // Build the list of .nupkg files to push, with a deterministic order:
        //   core (if present) -> RID packages (alphabetical by RID) -> meta (if present)
        List<string> packagesToPush;

        if (localTargets is { Count: > 0 })
        {
            if (localTargets.FirstOrDefault(static p => string.IsNullOrWhiteSpace(p.Value)) is { Key: { } rid, Value: not null } /* <- essentially a "not-default" KVP */)
            { return await logger.FailAsync($"Invalid cache mapping: target '{rid}' has an empty package path.", cancellationToken); }

            packagesToPush = [];

            if (localTargets.TryGetValue("core", out var nupkg)) { packagesToPush.Add(nupkg); }
            packagesToPush.AddRange(localTargets
                .Where(static p => p.Key is not ("core" or "meta"))
                .OrderBy(static p => p.Key, StringComparer.Ordinal) // stable, deterministic
                .Select(static p => p.Value)
            );
            if (localTargets.TryGetValue("meta", out nupkg)) { packagesToPush.Add(nupkg); }
        }
        else
        {
            // No authoritative mapping (likely due to --no-pack). Fallback:
            // - If user targets are null/empty/contain "all": push every *.nupkg in outputDir.
            // - If user specified particular targets (e.g., "win-x64"), we cannot reliably map RIDs
            //   to filenames without a cache. Fail with a clear message.
            var pushAll = targets.Length is not > 0 || targets.Contains("all");
            if (!pushAll) {  return await logger.FailAsync($"Cannot map the requested targets to package files without a cache. Run '{packCommand.Name}' first or omit '{noPackOption.Name}'.", cancellationToken); }

            if (!Directory.Exists(outputDir)) { return await logger.FailAsync($"Output directory '{outputDir}' does not exist.", cancellationToken); }

            packagesToPush = [..Directory.EnumerateFiles(outputDir, "*.nupkg", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal)];

            if (packagesToPush.Count is not > 0) { return await logger.FailAsync($"No packages found in '{outputDir}'. Consider running '{packCommand.Name}' first.", cancellationToken); }
        }

        await logger.OutputVerboseAsync(() => $"Packages to push: {string.Join(", ", packagesToPush.Select(static p => $"'{p}'"))} ({packagesToPush.Count})", cancellationToken);

        var apiKeyMasked = new string('*', apiKey.Length);
        var pushed = 0;
        foreach (var nupkg in packagesToPush)
        {
            await logger.OutputAsync($"Pushing package '{Path.GetFileName(nupkg)}' to '{nugetSource}'...", cancellationToken);

            // Run dotnet nuget push
            await logger.OutputDotnetCliAsync([ "nuget", "push", nupkg ], [
                "--api-key", apiKeyMasked,
                "--source", nugetSource,                
                "--skip-duplicate"
            ], config: null, noRestore: false, noLogo: false, properties: [], cancellationToken);

            exit = await RunDotnetAsync([ "nuget", "push", nupkg ], [
                "--api-key", apiKey,
                "--source", nugetSource,                
                "--skip-duplicate"
            ], config: null, noRestore: false, noLogo: false /* "dotnet nuget" doesn't actually accept a "--no-logo" option */, properties: [], @out: null, @error: null, cancellationToken);

            await logger.OutputDotnetFinishedAsync([ "nuget", "push", nupkg ], exit, cancellationToken);

            if (exit is not 0) { return exit; }

            pushed++;
            await logger.OutputVerboseAsync(() => $"Package '{Path.GetFileName(nupkg)}' pushed successfully.", cancellationToken);

            // Optional symbols push (same API key/source for nuget.org). Only for existing .snupkg.
            if (pushSymbols && Path.ChangeExtension(nupkg, ".snupkg") is var snupkg && File.Exists(snupkg))
            {
                await logger.OutputAsync($"Pushing symbols package '{Path.GetFileName(snupkg)}' to '{nugetSource}'...", cancellationToken);

                // Run dotnet nuget push
                await logger.OutputDotnetCliAsync([ "nuget", "push", snupkg ], [
                    "--api-key", apiKeyMasked,
                    "--source", nugetSource,                
                    "--skip-duplicate"
                ], config: null, noRestore: false, noLogo: false, properties: [], cancellationToken);

                exit = await RunDotnetAsync([ "nuget", "push", snupkg ], [
                    "--api-key", apiKey,
                    "--source", nugetSource,                
                    "--skip-duplicate"
                ], config: null, noRestore: false, noLogo: false /* "dotnet nuget" doesn't actually accept a "--no-logo" option */, properties: [], @out: null, @error: null, cancellationToken);

                await logger.OutputDotnetFinishedAsync([ "nuget", "push", snupkg ], exit, cancellationToken);               

                if (exit is not 0) { return exit; }

                pushed++;
                await logger.OutputVerboseAsync(() => $"Symbols package '{Path.GetFileName(snupkg)}' pushed successfully.", cancellationToken);
            }
        }        

        await logger.OutputAsync($"Push complete. Pushed {pushed} package(s).", cancellationToken);

        return 0;
    }
}

const string DocsOutputPropertyName    = "docsOutput",
             DocsApiOutputPropertyName = "docsApiOutput";
string? GetDocsApiOutput(Options options) => options.GetString(docsApiOutputOption, DocsApiOutputPropertyName);
string? GetDocsOutput   (Options options) => options.GetString(docsOutputOption,    DocsOutputPropertyName);

async Task<int> HandleDocsCleanAsync(Logger logger, Options options, FileInfo docsFile, bool noRestore, bool noLogo, CancellationToken cancellationToken)
{
    var docsApiOutput = GetDocsApiOutput(options);
    var docsOutput = GetDocsOutput(options);

    List<string> dirsToClean  = [];    

    var docsApiOutputEmpty = string.IsNullOrWhiteSpace(docsApiOutput);
    if (!docsApiOutputEmpty) { dirsToClean.Add(docsApiOutput!); }

    var docsOutputEmpty = string.IsNullOrWhiteSpace(docsOutput);
    if (!docsOutputEmpty) { dirsToClean.Add(docsOutput!); }

    // if necessary, parse the docfx.json docsFile for "dest.api", "build.output", "build.debugOutput", "build.rawModelOutputFolder", or "build.viewModelOutputFolder"
    if (docsApiOutputEmpty || docsOutputEmpty)
    {
        try
        {
            await using var stream = docsFile.OpenRead();
            using var jsonDocument = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);            
            var docsFileContainingDir = docsFile.Directory?.FullName ?? string.Empty;

            if (docsApiOutputEmpty
                && jsonDocument.RootElement.TryGetProperty("dest", out var dest))
            {
                if (dest.TryGetProperty("api", out var api) && api.ValueKind is JsonValueKind.String && api.GetString() is var dirToClean && !string.IsNullOrWhiteSpace(dirToClean))
                { dirsToClean.Add(Path.Combine(docsFileContainingDir, dirToClean)); /* relative path values in a 'docfx.json' are always relative to the 'docfx.json' file */ }
            }

            if (docsOutputEmpty
                && jsonDocument.RootElement.TryGetProperty("build", out var build))
            {
                foreach (var property in (ReadOnlySpan<string>)[ "output", "debugOutput", "rawModelOutputFolder", "viewModelOutputFolder" ])
                {
                    if (build.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String && value.GetString() is var dirToClean && !string.IsNullOrWhiteSpace(dirToClean))
                    { dirsToClean.Add(Path.Combine(docsFileContainingDir, dirToClean)); /* relative path values in a 'docfx.json' are always relative to the 'docfx.json' file */ }
                }
            }
        }
        catch (JsonException) { return await logger.FailAsync($"The docfx.json file '{docsFile.FullName}' contains invalid JSON.", cancellationToken); }
        catch (IOException e) { return await logger.FailAsync($"Failed to read docfx.json '{docsFile.FullName}': [{e.GetType().Name}]: {e.Message}", cancellationToken); }
    }
    

    if (dirsToClean.Count is not > 0) { return await logger.FailAsync($"No documentation output directories could be resolved. Provide {docsOutputOption.Name}, set '{DocsOutputPropertyName}' in config, or define 'build.output' in docfx.json.", cancellationToken); }

    var exit = 0;
    foreach (var dirToClean in dirsToClean) { exit = await DeleteDirectoryAsync(dirToClean, exit, logger, cancellationToken); }

    return exit;
}

async Task<int> HandleDocsAsync(Logger logger, Options options, FileInfo docsFile, bool noRestore, bool noLogo, CancellationToken cancellationToken)
{
    var docsApiOutput          = GetDocsApiOutput(options);
    var docsOutput             = GetDocsOutput(options);
    var buildBeforeDocs        = options.GetBoolean(buildBeforeDocsOption, "buildBeforeDocs", false);
    var requireDocFxMinVersion = options.GetString(requireDocFxMinVersionOption, "requireDocfxMinVersion");
    var withoutMetadata        = options.ParseResult.GetValue(withoutMetadataOption) ?? false;
    var withoutBuild           = options.ParseResult.GetValue(withoutBuildOption) ?? false;

    int exit;
    if (buildBeforeDocs)
    {
        await logger.OutputAsync("Building project before generating docs...", cancellationToken);
        exit = await ProjectSetupAsync(HandleBuildAsync)(logger, options, noRestore, noLogo, cancellationToken);
        if (exit is not 0) { return exit; }
    }

    NuGetVersion? docFxVersion = null;
    exit = await RunProcessAsync("dotnet", [ "tool", "run", "docfx", "--version" ],
        async (procOut, cancellationToken) =>
        {
            var line = await procOut.ReadLineAsync(cancellationToken);
            if (line is null) { await Task.Yield(); return false; }
            if (NuGetVersion.TryParse(line, out var version)) { docFxVersion = version; }
            return true;
        },
        errorReaderAsync: null,    
        cancellationToken
    ); 

    if (exit is not 0)
    {
        await logger.ErrorAsync( $"""
            'dotnet tool run docfx --version' failed with exit code {exit}.
            DocFx might not be installed as a local dotnet cli tool.
            """, cancellationToken);
    }
    else if (docFxVersion is null)
    {
        exit = 1;
        await logger.ErrorAsync("""
            Failed to parse DocFX version from 'dotnet tool run docfx --version'.
            Ensure DocFX is correctly installed as a local dotnet cli tool.
            """, cancellationToken);
    }
    else if (NuGetVersion.TryParse(requireDocFxMinVersion, out var docFxMinVersion) && docFxMinVersion > docFxVersion)
    {
        exit = 1;
        await logger.ErrorAsync("""
            Installed DocFX version ({docFxVersion}) does not meet the minimum required version ({docFxMinVersion}).
            Please update your local dotnet cli DocFX tool.
            """, cancellationToken);
    }
    if (exit is not 0)
    {
        return await logger.FailAsync("""
            To install or update DocFX as a local dotnet cli tool, you can run
              dotnet tool install docfx --local
            or
              dotnet tool update docfx --local
            """, errorCode: exit, cancellationToken);
    }

    await logger.OutputAsync($"Generating docs for '{docsFile.FullName}'...", cancellationToken);    

    if (!withoutMetadata)
    {
        (var outputSwitch, docsApiOutput) = !string.IsNullOrWhiteSpace(docsApiOutput)
            ? ("--output", Path.GetFullPath(docsApiOutput))
            : (null, null);

        await logger.OutputDotnetCliAsync([ "tool", "run", "docfx", "metadata", docsFile.FullName ], [
            outputSwitch, docsApiOutput
        ], config: null, noRestore: false, noLogo: false, properties: [], cancellationToken);

        exit = await RunDotnetAsync([ "tool", "run", "docfx", "metadata", docsFile.FullName ], [
            outputSwitch, docsApiOutput
        ], config: null /* "docfx" doesn't accept a "-c" option */, noRestore: false, noLogo: false /* "docfx" doesn't actually accept a "--no-logo" option */, properties: [], @out: null, @error: null, cancellationToken);

        await logger.OutputDotnetFinishedAsync([ "tool", "run", "docfx", "metadata", docsFile.FullName ], exit, cancellationToken); 

        if (exit is not 0) { return exit; }
    }

    if (!withoutBuild)
    {
        (var outputSwitch, docsOutput) = !string.IsNullOrWhiteSpace(docsOutput)
            ? ("--output", Path.GetFullPath(docsOutput))
            : (null, null);

        await logger.OutputDotnetCliAsync([ "tool", "run", "docfx", "build", docsFile.FullName ], [
            outputSwitch, docsOutput
        ], config: null, noRestore: false, noLogo: false, properties: [], cancellationToken);

        exit = await RunDotnetAsync([ "tool", "run", "docfx", "build", docsFile.FullName ], [
            outputSwitch, docsOutput
        ], config: null /* "docfx" doesn't accept a "-c" option */, noRestore: false, noLogo: false /* "docfx" doesn't actually accept a "--no-logo" option */, properties: [], @out: null, @error: null, cancellationToken);

        await logger.OutputDotnetFinishedAsync([ "tool", "run", "docfx", "build", docsFile.FullName ], exit, cancellationToken);   
    }

    return exit;
}

async Task<int> HandleNCoverAsync(Logger logger, Options options, FileInfo projectFile, bool noRestore, bool noLogo, Shared<HttpClient> httpClient, Shared<TempDirectory> tempDirectory, CancellationToken cancellationToken)
{
    const string nCoverExcludeFilePropertyName = "nCoverExcludeFile", nCoverExcludePropertyName = "nCoverExclude";

    var config             = options.ParseResult.GetValue(configOption)              ?? "Debug";
    var defines            = options.ParseResult.GetValue(defineOption)              ?? [];
    var properties         = options.ParseResult.GetValue(propertyOption)            ?? [];
    var nCoverExcludeFile  = options.GetFileSystemInfo(nCoverExcludeFileOption, nCoverExcludeFilePropertyName);
    var nCoverExclude      = options.GetStringArray(nCoverExcludeOption, nCoverExcludePropertyName);
    var nCoverMinSeverity  = options.ParseResult.GetValue(nCoverMinSeverityOption);
    var nCoverSlightAsWarn = options.ParseResult.GetValue(nCoverSlightAsWarnOption);
    var nCoverWarnAsError  = options.ParseResult.GetValue(nCoverWarnAsErrorOption);
    var nCoverJsonOutput   = options.ParseResult.GetValue(nCoverJsonOutputOption);
    var nCoverNoUnicode    = options.ParseResult.GetValue(nCoverNoUnicodeOption);
    var nCoverNoAnsi       = options.ParseResult.GetValue(nCoverNoAnsiOption);
    var nCoverVerbosity    = options.ParseResult.GetValue(nCoverVerbosityOption)     ?? "stats";
    var nCoverPretty       = options.ParseResult.GetValue(nCoverPrettyOption);

    if (!(await DownloadRuntimesAsync(retrieveLicenseInfo: false, logger, options, httpClient, tempDirectory, cancellationToken)).TryGetValueOrElseError(out var runtimes, out var exit)) { return exit; }
    var (runtimesPath, avaiableRids, _, _) = runtimes;

    bool isWinX64;
    string nativeFile;
    if (avaiableRids.Contains("win-x64")
        && Path.Combine(runtimesPath, "win-x64", "native") is var nativeX64Path
        && Directory.Exists(nativeX64Path)
        && (nativeFile = Directory.GetFiles(nativeX64Path).FirstOrDefault()!) is not null)
    {
        isWinX64 = true;
        await logger.OutputVerboseAsync(() => "RID 'win-x64' is available; 'win-x64' will be used for coverage analysis.", cancellationToken);
    }
    else if (avaiableRids.Contains("win-x86")
        && Path.Combine(runtimesPath, "win-x86", "native") is var nativeX86Path
        && Directory.Exists(nativeX86Path)
        && (nativeFile = Directory.GetFiles(nativeX86Path).FirstOrDefault()!) is not null)
    {
        isWinX64 = false;
        await logger.OutputVerboseAsync(() => "RID 'win-x86' is available; 'win-x86' will be used for coverage analysis.", cancellationToken);
    }
    else { return await logger.FailAsync($"Neither 'win-x64' nor 'win-x86' RIDs are available in the downloaded runtimes. Cannot proceed with coverage analysis. Available RIDs: {string.Join(", ", avaiableRids)}.", cancellationToken); }    

    nativeFile = Path.GetFullPath(nativeFile);

    // Run dotnet build first to potentially build the project
    await logger.OutputDotnetCliAsync([ "build", projectFile.FullName ], [
        defines.Length is > 0 ? $"/p:DefineConstants={string.Join(";", defines)}" : null,
        "-r", isWinX64 ? "win-x64" : "win-x86",
        "-v", "quiet", // we want to minimize the cluttered output from the build (epspecially the "restore" part); the overall gaol of this subcommand is to run ncover.cs, not to provide a detailed build log
    ], config, noRestore, noLogo, properties, cancellationToken);

    exit = await RunDotnetAsync([ "build", projectFile.FullName ], [
        defines.Length is > 0 ? $"/p:DefineConstants={string.Join(";", defines)}" : null,
        "-r", isWinX64 ? "win-x64" : "win-x86",
        "-v", "quiet", // we want to minimize the cluttered output from the build (epspecially the "restore" part); the overall gaol of this subcommand is to run ncover.cs, not to provide a detailed build log
    ], config, noRestore, noLogo, properties, @out: null, @error: null, cancellationToken);

    await logger.OutputDotnetFinishedAsync([ "build", projectFile.FullName ], exit, cancellationToken);

    if (exit is not 0) { return exit; }

    // Run dotnet build a second time to get the output assembly path
    string? managedFile = null;
    exit = await RunProcessAsync("dotnet", [ "build",
            projectFile.FullName,
            defines.Length is > 0 ? $"/p:DefineConstants={string.Join(";", defines)}" : null,
            "-r", isWinX64 ? "win-x64" : "win-x86",
            config is not null ? "-c" : null, config,
            "--no-restore",
            "--no-logo",
            ..properties.Select(static prop => $"/p:{prop}"),
            "-getProperty:TargetPath" // <- this will get us the path we're looking for printed to the standard output
        ],
        async (procOut, cancellationToken) =>
        {
            var line = await procOut.ReadLineAsync(cancellationToken);
            if (line is null) { await Task.Yield(); return false; }
            line = line.Trim();
            if (File.Exists(line)) { managedFile = line; }
            return true;
        },
        errorReaderAsync: null,
        cancellationToken
    );

    if (exit is not 0) { return await logger.FailAsync($"Failed to run 'dotnet build -getProperty:TargetPath'.", errorCode: exit, cancellationToken); }
    else if (string.IsNullOrWhiteSpace(managedFile)) { return await logger.FailAsync($"Build output assembly not found at expected path: '{managedFile}'.", cancellationToken); }

    managedFile = Path.GetFullPath(managedFile);

    // Run dotnet run ncover.cs
    await logger.OutputDotnetCliAsync([ "run", nCoverFile.FullName ], [
        "--",
        nativeFile,
        managedFile,
        nCoverVerbosityOption.Name, nCoverVerbosity,
        nCoverMinSeverity is not null ? nCoverMinSeverityOption.Name : null, nCoverMinSeverity,
        nCoverSlightAsWarn is not null ? nCoverSlightAsWarnOption.Name : null, nCoverSlightAsWarn switch { true => "true", false => "false", _ => null },
        nCoverWarnAsError is not null ? nCoverWarnAsErrorOption.Name : null, nCoverWarnAsError switch { true => "true", false => "false", _ => null },
        nCoverNoUnicode is not null ? nCoverNoUnicodeOption.Name : null, nCoverNoUnicode switch { true => "true", false => "false", _ => null },
        nCoverNoAnsi is not null ? nCoverNoAnsiOption.Name : null, nCoverNoAnsi switch { true => "true", false => "false", _ => null },
        nCoverJsonOutput is not null ? nCoverJsonOutputOption.Name : null, nCoverJsonOutput?.FullName,
        nCoverPretty is not null ? nCoverPrettyOption.Name : null, nCoverPretty switch { true => "true", false => "false", _ => null },
        nCoverExcludeFile is not null ? nCoverExcludeFileOption.Name : null, nCoverExcludeFile?.FullName,
        nCoverExclude is { Length: > 0} ? nCoverExcludeOption.Name : null, ..nCoverExclude ?? []
    ], config: null, noRestore: noRestore, noLogo: false /* dotnet run doesn't actually accept a "--no-logo" option */, properties: [], cancellationToken);

    exit = await RunDotnetAsync([ "run", nCoverFile.FullName ], [
        "--",
        nativeFile,
        managedFile,
        nCoverVerbosityOption.Name, nCoverVerbosity,
        nCoverMinSeverity is not null ? nCoverMinSeverityOption.Name : null, nCoverMinSeverity,
        nCoverSlightAsWarn is not null ? nCoverSlightAsWarnOption.Name : null, nCoverSlightAsWarn switch { true => "true", false => "false", _ => null },
        nCoverWarnAsError is not null ? nCoverWarnAsErrorOption.Name : null, nCoverWarnAsError switch { true => "true", false => "false", _ => null },
        nCoverNoUnicode is not null ? nCoverNoUnicodeOption.Name : null, nCoverNoUnicode switch { true => "true", false => "false", _ => null },
        nCoverNoAnsi is not null ? nCoverNoAnsiOption.Name : null, nCoverNoAnsi switch { true => "true", false => "false", _ => null },
        nCoverJsonOutput is not null ? nCoverJsonOutputOption.Name : null, nCoverJsonOutput?.FullName,
        nCoverPretty is not null ? nCoverPrettyOption.Name : null, nCoverPretty switch { true => "true", false => "false", _ => null },
        nCoverExcludeFile is not null ? nCoverExcludeFileOption.Name : null, nCoverExcludeFile?.FullName,
        nCoverExclude is { Length: > 0} ? nCoverExcludeOption.Name : null, ..nCoverExclude ?? []
    ], config: null, noRestore: noRestore, noLogo: false /* dotnet run doesn't actually accept a "--no-logo" option */, properties: [], @out: null, @error: null, cancellationToken);

    await logger.OutputDotnetFinishedAsync([ "run", nCoverFile.FullName ], exit, cancellationToken);

    return exit;
}

// ===== Process runners =====
static async Task<int> RunProcessAsync(string file, IEnumerable<string?> args, Func<TextReader, CancellationToken, Task<bool>>? outReaderAsync, Func<TextReader, CancellationToken, Task<bool>>? errorReaderAsync, CancellationToken cancellationToken)
{
    using var proc = new Process
    {
        StartInfo =
        {
            FileName = file,
            RedirectStandardOutput = outReaderAsync is not null,
            RedirectStandardError = errorReaderAsync is not null,
            UseShellExecute = false
        }
    };
    proc.StartInfo.ArgumentList.AddRange(args.OfType<string>());
    
    proc.Start();
    
    var stdOutTask = outReaderAsync is not null
        ? Task.Run(async () => { while (await outReaderAsync(proc.StandardOutput, cancellationToken) || !proc.HasExited); }, cancellationToken)
        : Task.CompletedTask;
    var stdErrTask = errorReaderAsync is not null
        ? Task.Run(async () => { while (await errorReaderAsync(proc.StandardError, cancellationToken) || !proc.HasExited); }, cancellationToken)
        : Task.CompletedTask;

    await Task.WhenAll(proc.WaitForExitAsync(cancellationToken), stdOutTask, stdErrTask);

    return proc.ExitCode;
}

static Task<int> RunDotnetAsync(ReadOnlySpan<string> commands, ReadOnlySpan<string?> args, string? config, bool noRestore, bool noLogo, IEnumerable<string> properties, TextWriter? @out, TextWriter? error, CancellationToken cancellationToken)
{
    const int defaultOutputBufferSize = 1024;

    return RunProcessAsync("dotnet", [ ..commands,
            config is not null ? "-c" : null, config,
            noRestore ? "--no-restore" : null,
            noLogo ? "--no-logo" : null,
            ..properties.Select(static prop => $"/p:{prop}"),
            ..args
        ],
        @out is not null
            ? GC.AllocateUninitializedArray<char>(defaultOutputBufferSize) switch { var buffer => async (procOut, cancellationToken) =>
            { 
                if (await @out.CopyAvailableTextFromAsync(procOut, buffer.AsMemory(), cancellationToken) is not > 0) { await Task.Yield(); return false; }
                return true;
            } }
            : null,
        error is not null
            ? GC.AllocateUninitializedArray<char>(defaultOutputBufferSize) switch { var buffer => async (procError, cancellationToken) =>
            { 
                if (await error.CopyAvailableTextFromAsync(procError, buffer.AsMemory(), cancellationToken) is not > 0) { await Task.Yield(); return false; }
                return true;
            } }
            : null,
        cancellationToken
    );
}

// ===== Helper methods =====
static async Task<int> DeleteDirectoryAsync(string directory, int exit, Logger logger, CancellationToken cancellationToken = default)
{
    if (Directory.Exists(directory))
    {
        try
        {
            await logger.OutputVerboseAsync(() => $" Attempting to delete directory: '{directory}'", cancellationToken);
            Directory.Delete(directory, true);
            await logger.OutputAsync($"Successfully deleted '{directory}'", cancellationToken);
        }
        catch (Exception e) { exit = await logger.FailAsync($"Failed to delete '{directory}': [{e.GetType().Name}]: {e.Message}", cancellationToken); }
    }
    return exit;
}

async Task<Result<(string RuntimesPath, string[] AvailableRids, string? RuntimesLicenseSpdx, string? RuntimesLicensePath)>> DownloadRuntimesAsync(bool retrieveLicenseInfo, Logger logger, Options options, Shared<HttpClient> httpClient, Shared<TempDirectory> tempDirectory, CancellationToken cancellationToken)
{
    var runtimesVersion            = GetRuntimesVersion(options);
    var runtimesUrl                = GetRuntimesUrl(options);
    var forceRuntimesDownload      = options.ParseResult.GetValue(forceRuntimesDownloadOption) ?? false;
    var runtimesLicenseSpdx        = options.GetString(runtimesLicenseSpdxOption,        "runtimesLicenseSpdx");
    var runtimesLicenseFileUrl     = options.GetString(runtimesLicenseFileUrlOption,     "runtimesLicenseFileUrl");
    var runtimesLicenseSpdxFileUrl = options.GetString(runtimesLicenseSpdxFileUrlOption, "runtimesLicenseSpdxFileUrl");
    var cacheDir                   = GetCacheDir(options);
    
    Directory.CreateDirectory(cacheDir);

    var tempDir = (await tempDirectory.GetValueAsync(cancellationToken)).Info.FullName;

    if (string.IsNullOrWhiteSpace(runtimesVersion)) { return await logger.FailAsync($"No runtimes version specified. Provide {runtimesVersionOption.Name} or set '{RuntimesVersionPropertyName}' in the config.", cancellationToken); }

    await logger.OutputVerboseAsync(() => $"Using runtimes version: {runtimesVersion}", cancellationToken);

    var runtimesCachePath = Path.Combine(cacheDir, "runtimes", runtimesVersion);
    Directory.CreateDirectory(runtimesCachePath);

    if (string.IsNullOrWhiteSpace(runtimesUrl)) { return await logger.FailAsync($"No runtimes URL specified. Provide {runtimesUrlOption.Name} or set '{RuntimesUrlPropertyName}' in the config. It may be a format string containing '{{0}}' for the version.", cancellationToken); }

    if (!(string.Format(runtimesUrl, runtimesVersion) is var runtimesUriString
        && Uri.TryCreate(runtimesUriString, UriKind.Absolute, out var runtimesUri))) { return await logger.FailAsync($"\"{runtimesUriString}\" is not a valid absolute URL.", cancellationToken); }

    var runtimesArchivePath = Path.GetFullPath(Path.Combine(runtimesCachePath, Path.GetFileName(runtimesUri.LocalPath) switch { var filename when !string.IsNullOrWhiteSpace(filename) => filename, _ => $"runtimes.zip" }));

    if (forceRuntimesDownload || !File.Exists(runtimesArchivePath))
    {
        if (!forceRuntimesDownload) { await logger.OutputVerboseAsync(() => $"No cached runtimes found under '{runtimesArchivePath}'.", cancellationToken); }
            
        await logger.OutputVerboseAsync(() => $"Using runtimes URL: {runtimesUriString}", cancellationToken);
        await logger.OutputAsync("Downloading runtimes archive...", cancellationToken);     

        await using (var httpStream = await (await httpClient.GetValueAsync(cancellationToken)).GetStreamAsync(runtimesUri, cancellationToken))
        await using (var fileStream = File.Create(runtimesArchivePath))
        { await httpStream.CopyToAsync(fileStream, cancellationToken); }

        await logger.OutputAsync("Download complete.", cancellationToken);
        await logger.OutputVerboseAsync(() => $"Saved runtimes archive to '{runtimesArchivePath}' ({new FileInfo(runtimesArchivePath).Length} bytes).", cancellationToken);
    }
    else { await logger.OutputVerboseAsync(() => $"Cached runtimes archive found under '{runtimesArchivePath}'.", cancellationToken); }
   
    string? runtimesLicensePath = null;
    if (retrieveLicenseInfo)
    {
        if (runtimesLicenseFileUrl is not null)
        {
            if (!(string.Format(runtimesLicenseFileUrl, runtimesVersion) is var licenseUriString
                && Uri.TryCreate(licenseUriString, UriKind.Absolute, out var licenseUri))) { return await logger.FailAsync($"\"{licenseUriString}\" is not a valid absolute URL.", cancellationToken); }

            runtimesLicensePath = Path.GetFullPath(Path.Combine(runtimesCachePath, Path.GetFileName(licenseUri.LocalPath) switch { var filename when !string.IsNullOrWhiteSpace(filename) => filename, _ => "LICENSE" }));

            if (forceRuntimesDownload || !File.Exists(runtimesLicensePath))
            {
                if (!forceRuntimesDownload) { await logger.OutputVerboseAsync(() => $"No cached license file found under '{runtimesLicensePath}'.", cancellationToken); }

                await logger.OutputVerboseAsync(() => $"Using license file URL: {licenseUriString}", cancellationToken);
                await logger.OutputAsync("Downloading license file...", cancellationToken);

                await using (var httpStream = await (await httpClient.GetValueAsync(cancellationToken)).GetStreamAsync(licenseUri, cancellationToken))
                await using (var fileStream = File.Create(runtimesLicensePath))
                { await httpStream.CopyToAsync(fileStream, cancellationToken); }

                await logger.OutputAsync("Download complete.", cancellationToken);
                await logger.OutputVerboseAsync(() => $"Saved license file to '{runtimesLicensePath}' ({new FileInfo(runtimesLicensePath).Length} bytes).", cancellationToken);
            }                
            else { await logger.OutputVerboseAsync(() => $"Cached license file found under '{runtimesLicensePath}'.", cancellationToken); }
        }
        
        string? runtimesLicenseSpdxPath = null;
        if (runtimesLicenseSpdxFileUrl is not null)
        {
            if (!(string.Format(runtimesLicenseSpdxFileUrl, runtimesVersion) is var licenseSpdxUriString
                && Uri.TryCreate(licenseSpdxUriString, UriKind.Absolute, out var licenseSpdxUri))) { return await logger.FailAsync($"\"{licenseSpdxUriString}\" is not a valid absolute URL.", cancellationToken); }

            runtimesLicenseSpdxPath = Path.GetFullPath(Path.Combine(runtimesCachePath, Path.GetFileName(licenseSpdxUri.LocalPath) switch
            {
                var filename when !string.IsNullOrWhiteSpace(filename) => filename,
                _ when !string.IsNullOrWhiteSpace(runtimesLicensePath) => $"{Path.GetFileName(runtimesLicensePath)}.spdx",
                _ => "LICENSE.spdx"
            }));
            
            if (forceRuntimesDownload || !File.Exists(runtimesLicenseSpdxPath))
            {
                if (!forceRuntimesDownload) { await logger.OutputVerboseAsync(() => $"No cached license SPDX file found under '{runtimesLicenseSpdxPath}'.", cancellationToken); }

                await logger.OutputVerboseAsync(() => $"Using license SPDX file URL: {licenseSpdxUriString}", cancellationToken);
                await logger.OutputAsync("Downloading license SPDX file...", cancellationToken);

                await using (var httpStream = await (await httpClient.GetValueAsync(cancellationToken)).GetStreamAsync(licenseSpdxUri, cancellationToken))
                await using (var fileStream = File.Create(runtimesLicenseSpdxPath))
                { await httpStream.CopyToAsync(fileStream, cancellationToken); }

                await logger.OutputAsync("Download complete.", cancellationToken);
                await logger.OutputVerboseAsync(() => $"Saved license SPDX file to '{runtimesLicenseSpdxPath}' ({new FileInfo(runtimesLicenseSpdxPath).Length} bytes).", cancellationToken);
            }
            else { await logger.OutputVerboseAsync(() => $"Cached license SPDX file found under '{runtimesLicenseSpdxPath}'.", cancellationToken); }
        }

        if (runtimesLicenseSpdxPath is not null && File.Exists(runtimesLicenseSpdxPath))
        {
            await using (var fileStream = File.OpenRead(runtimesLicenseSpdxPath))
            using (var reader = new StreamReader(fileStream))
            { runtimesLicenseSpdx = (await reader.ReadToEndAsync(cancellationToken)).Trim(); }
            
            await logger.OutputVerboseAsync(() => $"Read license SPDX identifier: {runtimesLicenseSpdx}.", cancellationToken);
        }

        if (runtimesLicensePath is not null && runtimesLicenseSpdx is not null)
        { await logger.OutputVerboseAsync(() => "Warning: Runtimes license SPDX identifier set and runtimes license file set. The SPDX identifier will be used for the RID packages license. The license file will still be included in the RID packages.", cancellationToken); }
    }

    var runtimesExtractPath = Path.Combine(tempDir, "runtimes", runtimesVersion);
    Directory.CreateDirectory(runtimesExtractPath);

    try { ZipFile.ExtractToDirectory(runtimesArchivePath, runtimesExtractPath, overwriteFiles: true); }
    catch (Exception e) { return await logger.FailAsync($"Failed to extract runtimes archive '{runtimesArchivePath}' to '{runtimesExtractPath}': [{e.GetType().Name}]: {e.Message}", cancellationToken); }

    await logger.OutputVerboseAsync(() => $"Extracted runtimes to '{runtimesExtractPath}'.", cancellationToken);

    var runtimesPath = Path.Combine(runtimesExtractPath, "runtimes");

    return (
        RuntimesPath: runtimesPath,
        AvailableRids: Directory.Exists(runtimesPath)
            ? [..Directory.GetDirectories(runtimesPath)
                .Select(static p => Path.GetFileName(p).NormalizeLower())
                .Where(static r => !string.IsNullOrEmpty(r))]
            : [],
        RuntimesLicenseSpdx: runtimesLicenseSpdx, RuntimesLicensePath: runtimesLicensePath
    );
}

// ===== Handler prototypes =====
file delegate Task<int> HandlerAsync(Logger logger, Options options, bool noRestore, bool noLogo, CancellationToken cancellationToken);
file delegate Task<int> ProjectHandlerAsync(Logger logger, Options options, FileInfo projectFile, bool noRestore, bool noLogo, Shared<HttpClient> httpClient, Shared<TempDirectory> tempDirectory, CancellationToken cancellationToken);
file delegate Task<int> DocsHandlerAsync(Logger logger, Options options, FileInfo docsFile, bool noRestore, bool noLogo, CancellationToken cancellationToken);

// ===== Logging =====
file readonly record struct Logger(TextWriter Out, TextWriter Error, bool IsVerbose)
{
    private static Task LogAsync(TextWriter writer, string message, CancellationToken cancellationToken)
        => writer.WriteLineAsync(message.AsMemory(), cancellationToken);

    public readonly Task OutputAsync(string message, CancellationToken cancellationToken = default)
        => LogAsync(Out, message, cancellationToken);

    public readonly Task OutputConditionalAsync(bool condition, Func<string> messageFactory, CancellationToken cancellationToken = default)
        => condition ? OutputAsync(messageFactory(), cancellationToken) : Task.CompletedTask;

    public readonly Task OutputConditionalAsync(bool condition, Func<CancellationToken, Task<string>> messageFactoryAsync, CancellationToken cancellationToken = default)
        => this switch
        {
            var logger when condition => messageFactoryAsync(cancellationToken)
                .ContinueWith(task => task.IsCompletedSuccessfully
                    ? logger.OutputAsync(task.Result, cancellationToken)
                    : Task.CompletedTask
                ).Unwrap(),
            _ => Task.CompletedTask
        };

    public readonly Task OutputConditionalAsync<TArg>(bool condition, Func<TArg, string> messageFactory, TArg arg, CancellationToken cancellationToken = default)
        where TArg : allows ref struct
        => condition ? OutputAsync(messageFactory(arg), cancellationToken) : Task.CompletedTask;

    public readonly Task OutputConditionalAsync<TArg>(bool condition, Func<TArg, CancellationToken, Task<string>> messageFactoryAsync, TArg arg, CancellationToken cancellationToken = default)
        where TArg : allows ref struct
        => this switch
        {
            var logger when condition => messageFactoryAsync(arg, cancellationToken)
                .ContinueWith(task => task.IsCompletedSuccessfully
                    ? logger.OutputAsync(task.Result, cancellationToken)
                    : Task.CompletedTask
                ).Unwrap(),
            _ => Task.CompletedTask
        };

    public readonly Task OutputVerboseAsync(Func<string> messageFactory, CancellationToken cancellationToken = default)
        => OutputConditionalAsync(IsVerbose, messageFactory, cancellationToken);

    public readonly Task OutputVerboseAsync(Func<CancellationToken, Task<string>> messageFactoryAsync, CancellationToken cancellationToken = default)
        => OutputConditionalAsync(IsVerbose, messageFactoryAsync, cancellationToken);

    public readonly Task OutputVerboseAsync<TArg>(Func<TArg, string> messageFactory, TArg arg, CancellationToken cancellationToken = default)    
        where TArg : allows ref struct
        => OutputConditionalAsync(IsVerbose, messageFactory, arg, cancellationToken);

    public readonly Task OutputVerboseAsync<TArg>(Func<TArg, CancellationToken, Task<string>> messageFactoryAsync, TArg arg, CancellationToken cancellationToken = default)
        where TArg : allows ref struct
        => OutputConditionalAsync(IsVerbose, messageFactoryAsync, arg, cancellationToken);

    public readonly Task ErrorAsync(string message, CancellationToken cancellationToken = default)
        => LogAsync(Error, message, cancellationToken);

    public readonly Task ErrorConditionalAsync(bool condition, Func<string> messageFactory, CancellationToken cancellationToken = default)
        => condition ? ErrorAsync(messageFactory(), cancellationToken) : Task.CompletedTask;

    public readonly Task ErrorConditionalAsync(bool condition, Func<CancellationToken, Task<string>> messageFactoryAsync, CancellationToken cancellationToken = default)
        => this switch
        {
            var logger when condition => messageFactoryAsync(cancellationToken)
                .ContinueWith(task => task.IsCompletedSuccessfully
                    ? logger.ErrorAsync(task.Result, cancellationToken)
                    : Task.CompletedTask
                ).Unwrap(),
            _ => Task.CompletedTask
        };

    public readonly Task ErrorConditionalAsync<TArg>(bool condition, Func<TArg, string> messageFactory, TArg arg, CancellationToken cancellationToken = default)
        where TArg : allows ref struct
        => condition ? ErrorAsync(messageFactory(arg), cancellationToken) : Task.CompletedTask;

    public readonly Task ErrorConditionalAsync<TArg>(bool condition, Func<TArg, CancellationToken, Task<string>> messageFactoryAsync, TArg arg, CancellationToken cancellationToken = default)
        where TArg : allows ref struct
        => this switch
        {
            var logger when condition => messageFactoryAsync(arg, cancellationToken)
                .ContinueWith(task => task.IsCompletedSuccessfully
                    ? logger.ErrorAsync(task.Result, cancellationToken)
                    : Task.CompletedTask
                ).Unwrap(),
            _ => Task.CompletedTask
        };

    public readonly Task ErrorVerboseAsync(Func<string> messageFactory, CancellationToken cancellationToken = default)
        => ErrorConditionalAsync(IsVerbose, messageFactory, cancellationToken);

    public readonly Task ErrorVerboseAsync(Func<CancellationToken, Task<string>> messageFactoryAsync, CancellationToken cancellationToken = default)
        => ErrorConditionalAsync(IsVerbose, messageFactoryAsync, cancellationToken);

    public readonly Task ErrorVerboseAsync<TArg>(Func<TArg, string> messageFactory, TArg arg, CancellationToken cancellationToken = default)
        where TArg : allows ref struct
        => ErrorConditionalAsync(IsVerbose, messageFactory, arg, cancellationToken);

    public readonly Task ErrorVerboseAsync<TArg>(Func<TArg, CancellationToken, Task<string>> messageFactoryAsync, TArg arg, CancellationToken cancellationToken = default)
        where TArg : allows ref struct
        => ErrorConditionalAsync(IsVerbose, messageFactoryAsync, arg, cancellationToken);

    public readonly async Task<int> FailAsync(string message, int errorCode, CancellationToken cancellationToken = default)
    {
        await ErrorAsync(message, cancellationToken);
        return errorCode;
    }

    public readonly Task<int> FailAsync(string message, CancellationToken cancellationToken = default)
        => FailAsync(message, errorCode: 1, cancellationToken);
}

// ===== Logging Helpers =====
file static class LoggerExtensions
{
    private readonly ref struct CommandInfo(ReadOnlySpan<string> commands, ReadOnlySpan<string?> args)
    {       
        public readonly ReadOnlySpan<string> Commands = commands;
        public readonly ReadOnlySpan<string?> Args = args;
    }

    public static Task OutputDotnetCliAsync(in this Logger logger, ReadOnlySpan<string> commands, ReadOnlySpan<string?> args, string? config, bool noRestore, bool noLogo, IEnumerable<string> properties, CancellationToken cancellationToken = default)
    {
        return logger.OutputVerboseAsync(info => $"Running dotnet {string.Join(" ", ((IEnumerable<string?>)[ ..info.Commands,
            config is not null ? "-c" : null, config,
            noRestore ? "--no-restore" : null,
            noLogo ? "--no-logo" : null,
            ..properties.Select(static prop => $"/p:{prop}"),
            ..info.Args
        ]).OfType<string>().Select(quote))}", new CommandInfo(commands, args), cancellationToken);

        [return: NotNull] static string quote([NotNull] string value) => value switch { [] => "\"\"", ['"', .., '"'] => value, _ when value.Any(char.IsWhiteSpace) => $"\"{escape(value)}\"", _ => escape(value) };
        [return: NotNull] static string escape([NotNull] string value) => value.Replace("\"", "\\\"") /* escape containing double quotes */ switch { [.., '\\'] s => $"{s}\\" /* Windows paths: escape trailing backslash */, var s => s };
    }

    public static Task OutputDotnetFinishedAsync(in this Logger logger, ReadOnlySpan<string> commands, int exitCode, CancellationToken cancellationToken = default)
        => logger.OutputVerboseAsync(commands => $"dotnet {string.Join(" ", commands)} finished with exit code {exitCode}.", commands, cancellationToken);
}

// ===== Options =====
file readonly record struct Options(ParseResult ParseResult, JsonDocument? JsonDocument)
{   
    [return: NotNullIfNotNull(nameof(path))]
    private static T? FromPath<T>(string? path)
        where T : notnull, FileSystemInfo
        => path switch
        {
            null => null,
            _ when File.Exists(path) && new FileInfo(path) is T file => file,
            _ when Directory.Exists(path) && new DirectoryInfo(path) is T directory => directory,
            _ when (typeof(T) == typeof(FileInfo) || Path.HasExtension(path)) && new FileInfo(path) is T file => file,
            _ when new DirectoryInfo(path) is T directory => directory,
            _ => null
        };

    public readonly bool? GetBoolean(Option<bool?> option, string propertyName)
        => ParseResult.GetValue(option) ?? (JsonDocument?.RootElement.TryGetProperty(propertyName, out var property) is true ? property.GetBoolean() : null);

    public readonly bool GetBoolean(Option<bool?> option, string propertyName, bool fallback)
        => GetBoolean(option, propertyName) ?? fallback;

    public readonly T? GetFileSystemInfo<T>(Option<T?> option, string propertyName)
        where T : notnull, FileSystemInfo
        => ParseResult.GetValue(option) ?? (JsonDocument?.RootElement.TryGetProperty(propertyName, out var property) is true ? FromPath<T>(property.GetString()) : null);

    public readonly T? GetFileSystemInfo<T>(Option<T?> option, string propertyName, string fallback)
        where T : notnull, FileSystemInfo
        => GetFileSystemInfo(option, propertyName) ?? FromPath<T>(fallback);

    public readonly string? GetString(Option<string?> option, string propertyName)        
        => ParseResult.GetValue(option) ?? (JsonDocument?.RootElement.TryGetProperty(propertyName, out var property) is true ? property.GetString() : null);

    public string GetString(Option<string?> option, string propertyName, string fallback)
        => GetString(option, propertyName) ?? fallback;

    public readonly string[]? GetStringArray(Option<string[]?> option, string propertyName)
        => ParseResult.GetValue(option)
        ?? (JsonDocument?.RootElement.TryGetProperty(propertyName, out var property) is true && property.ValueKind is JsonValueKind.Array
            ? [..property.EnumerateArray().Select(static str => str.GetString()).Where(static str => str is not null)]
            : null);

    public string[] GetStringArray(Option<string[]?> option, string propertyName, params string[] fallback)
        => GetStringArray(option, propertyName) ?? fallback;
}

// ===== Cache data JSON =====
internal readonly record struct CacheData(
    [property: JsonPropertyName("version")] Version                                   Version,
    [property: JsonPropertyName("inputs")]  CacheData.InputsData                      Inputs,
    [property: JsonPropertyName("targets")] ImmutableSortedDictionary<string, string> Targets
)
{
    internal readonly record struct InputsData(
        [property: JsonPropertyName("runtimesVersion")] string                     RuntimesVersion,
        [property: JsonPropertyName("runtimesUrl")]     string                     RuntimesUrl,
        [property: JsonPropertyName("config")]          string                     Config,
        [property: JsonPropertyName("noSymbols")]       bool                       NoSymbols,
        [property: JsonPropertyName("defines")]         ImmutableSortedSet<string> Defines,
        [property: JsonPropertyName("properties")]      ImmutableSortedSet<string> Properties
    );
}

[JsonSourceGenerationOptions()]
[JsonSerializable(typeof(CacheData))]
internal sealed partial class CacheDataSerializerContext : JsonSerializerContext;

// ===== Temporary directory abstraction =====
file sealed class TempDirectory : IDisposable
{
    public TempDirectory(string tempDir)
    {
        Info = new(tempDir);
        if (Info.Exists) { try { Info.Delete(recursive: true); } catch { } }
        Info.Create();
    }

    public DirectoryInfo Info { get; private set; }

    public void Dispose()
    {
        if (Info is null) { return; }
        if (Info.Exists) { try { Info.Delete(recursive: true); } catch { } }
        Info = null!;
    }
}

// ===== Helper types =====
file readonly struct Result<T>
{
    private readonly T? mValue;
    private readonly int? mError;

    public readonly bool IsSuccess => mError is null;

    private Result(T? value, int? error) { mValue = value; mError = error; }

    public static Result<T> Value(T value) => new(value, error: null);
    public static Result<T> Error(int error) => new(value: default, error);

    public readonly bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        if (IsSuccess) { value = mValue!; return true; }
        value = default!; return false;
    }

    public readonly bool TryGetError(out int error)
    {
        if (!IsSuccess) { error = mError.GetValueOrDefault(); return true; }
        error = default; return false;
    }

    public readonly bool TryGetValueOrElseError([MaybeNullWhen(false)] out T value, out int error)
    {
        if (IsSuccess) { value = mValue!; error = default; return true; }
        value = default!; error = mError.GetValueOrDefault(); return false;
    }

    public static implicit operator Result<T>(T value) => Value(value);

    public static implicit operator Result<T>(int error) => Error(error);
}

file sealed class Shared<T> : IAsyncDisposable, IDisposable
{
    private Func<CancellationToken, Task<T>>? mValueFactoryAsync;
    private T mValue = default!;
    private bool mIsDisposed = false;

    public Shared(Func<CancellationToken, Task<T>> valueFactoryAsync) => mValueFactoryAsync = valueFactoryAsync;

    public Shared(Func<T> valueFactory) : this(_ => Task.FromResult(valueFactory())) { }

#pragma warning disable CA2012 // I'm pretty sure that's an okay way of doing it
    public void Dispose() { if (DisposeAsync() is { IsCompleted: false } disposeTask) { disposeTask.AsTask().GetAwaiter().GetResult(); } }
#pragma warning restore CA2012 

    public async ValueTask DisposeAsync()
    {
        if (!mIsDisposed && mValueFactoryAsync is null)
        {
            switch (mValue)
            {                
                case IAsyncDisposable asyncDisposable: await asyncDisposable.DisposeAsync(); break;
                case IDisposable disposable: disposable.Dispose(); break;
            }
            mValue = default!;
            mIsDisposed = true;
        }
    }

    public async Task<T> GetValueAsync(CancellationToken cancellationToken = default)
    {
        if (mValueFactoryAsync is not null)
        {
            mValue = await mValueFactoryAsync(cancellationToken);
            mValueFactoryAsync = null;
        }

        return mValue;
    }
}

file sealed class FileSystemInfoEqualityComparer : IEqualityComparer<FileSystemInfo>
{
    private FileSystemInfoEqualityComparer() { }    

    public static FileSystemInfoEqualityComparer Instance { get; } = new();

    [return: NotNullIfNotNull(nameof(info))]
    private static string? NormalizeFullPath(FileSystemInfo? info) => info is not null
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(info.FullName))
        : null;

    public bool Equals(FileSystemInfo? x, FileSystemInfo? y) => OperatingSystem.IsWindows()
        ? string.Equals(NormalizeFullPath(x), NormalizeFullPath(y), StringComparison.OrdinalIgnoreCase)
        : string.Equals(NormalizeFullPath(x), NormalizeFullPath(y), StringComparison.Ordinal);

    public int GetHashCode([DisallowNull] FileSystemInfo obj) => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase.GetHashCode(NormalizeFullPath(obj))
        : StringComparer.Ordinal.GetHashCode(NormalizeFullPath(obj));
}

file static class Extensions
{
    public interface IDispatchComparable<T> where T : IComparable<T>;
    public interface IDispatchComparisonOperators<T> where T : IComparisonOperators<T, T, bool>;

    public static void AddRange<T>(this ICollection<T> coll, IEnumerable<T> items) { foreach (var item in items) { coll.Add(item); } }

    public static async Task<int> CopyAvailableTextFromAsync(this TextWriter dest, TextReader src, Memory<char> buffer, CancellationToken cancellationToken = default)
    {
        var charsRead = await src.ReadAsync(buffer, cancellationToken);
        if (charsRead is > 0) { await dest.WriteAsync(buffer[..charsRead], cancellationToken); }
        return charsRead;
    }

    public static async Task<(string Id, string Version)?> GetNuGetPackageIdentityAsync(this FileInfo? nupkgFile, CancellationToken cancellationToken = default)
    {
        if (nupkgFile is { Exists: true, FullName: var fullName })
        {
            await using var file = File.OpenRead(fullName);
            using var reader = new PackageArchiveReader(file);
            if (await reader.GetIdentityAsync(cancellationToken) is { Id: var id, HasVersion: true, Version: var version }) { return (id, version.ToNormalizedString()); }
        }
        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static T Max<T>(this T value, T other, IDispatchComparable<T>? _ = default) where T : IComparable<T> => value.CompareTo(other) is >= 0 ? value : other;

    [OverloadResolutionPriority(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static T Max<T>(this T value, T other, IDispatchComparisonOperators<T>? _ = default) where T : IComparisonOperators<T, T, bool> => value >= other ? value : other;

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static T Min<T>(this T value, T other, IDispatchComparable<T>? _ = default) where T : IComparable<T> => value.CompareTo(other) is <= 0 ? value : other;

    [OverloadResolutionPriority(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static T Min<T>(this T value, T other, IDispatchComparisonOperators<T>? _ = default) where T : IComparisonOperators<T, T, bool> => value <= other ? value : other;

    [return: NotNullIfNotNull(nameof(value))]
    public static string? NormalizeLower(this string? value)
    {
        if (value is null) { return null; }

        var valueSpan = value.AsSpan().Trim();
        return string.Create(valueSpan.Length, valueSpan, create);

        static void create(Span<char> dest, ReadOnlySpan<char> src) => src.ToLowerInvariant(dest);
    }

    public static bool TrySplitFirst(this string? value, char separator, [NotNullIfNotNull(nameof(value)), NotNullWhen(true)] out string? head, [NotNullWhen(true)] out string? tail)
    {
        if (value is null) { head = null; tail = null; return false; }

        var span = value.AsSpan();
        var idx = span.IndexOf(separator);

        if (idx is < 0) { head = value; tail = null; return false; }

        head = span[..idx].ToString(); tail = span[(idx + 1)..].ToString(); return true;
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static string? TruncateToMaxLength(this string? value, int maxLength)
    {
        if (maxLength is < 0) { return value; }
        if (value is null) { return null; }
        if (maxLength is 0) { return string.Empty; }
        if (value.Length <= maxLength) { return value; }

        return string.Create(maxLength, value.AsSpan(), create);

        static void create(Span<char> dest, ReadOnlySpan<char> src) => src[..dest.Length].CopyTo(dest);
    }
}
