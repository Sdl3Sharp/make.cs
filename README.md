# make.cs

make.cs is a minimal, file‑based C# build tool for managed projects with native runtime assets. It’s designed for multi‑flavor NuGet packaging (core, RID‑specific, meta) and was originally built to orchestrate the building and packaging of [SDL3#](https://github.com/Sdl3Sharp/SDL3Sharp) and related projects. The tool is intentionally generic and can be adapted to other .NET projects with similar needs. It is cache‑aware, reproducible, and extensible via MSBuild.

## Requirements

- .NET 10 SDK must be installed  
- The `dotnet` CLI must be available in your PATH  

## Running the tool

You can run `make.cs` in several ways. Example shown with the `build` subcommand:

**Normal invocation:**

```shell
dotnet run make.cs -- build
```

**Unix‑like systems (shell script):**

```shell
./make.sh build
```

If you get a permission error, run:

```shell
chmod +x make.sh
```

**PowerShell:**

```shell
./make.ps1 build
```

**Windows CMD:**

```shell
make.cmd build
```

The wrapper scripts simply forward arguments to the tool.

If you have issues running `make.cs`, try running `dotnet clean make.cs` followed by `dotnet restore make.cs`. Sometimes you have to do that twice for it to work.

## Commands, Options, and Examples

### Configuration file argument

Every invocation of `make.cs` can optionally take a **positional argument** that specifies a configuration file or a directory.  

- If a file is given, that file is used as the configuration.  
- If a directory is given, the tool looks for `make.json` inside that directory.  
- If omitted, the tool defaults to `make.json` in the current working directory.  

Example (using a custom config file):

```shell
./make.sh build ./myconfig.json
```

> [!NOTE]
> If a configuration file is found and used, the tool automatically changes the current working directory to the directory where the configuration file is located, for the lifetime of the tool.

### build

Builds the managed project. If `--project` is omitted, the tool looks for the first `.csproj` inside `./src`.

```shell
./make.sh build
```

| CLI option         | Config property | Description                                                    |
|--------------------|-----------------|----------------------------------------------------------------|
| `--project`        | `project`       | Path to .csproj or directory containing one.                   |
| `--configuration`  | (no config)     | Build configuration to use (for example `Debug` or `Release`). |
| `--define`         | (no config)     | One or more preprocessor symbols to define.                    |
| `--no-restore`     | (no config)     | Skip the restore phase.                                        |
| `--property`       | (no config)     | Additional MSBuild properties in the form `name=value`.        |
| `--verbose`        | (no config)     | Enable verbose logging.                                        |
| `--no-logo`        | `noLogo`        | Suppress startup banner.                                       |
| `--logo-file`      | `logoFile`      | Path to a text file containing startup logo ASCII art.         |

### pack

Packages NuGet artifacts: core, RID‑specific, and meta packages. If `--project` is omitted, the tool looks for the first `.csproj` inside `./src`.

```shell
./make.sh pack
```

| CLI option                          | Config property              | Description                                                    |
|-------------------------------------|------------------------------|----------------------------------------------------------------|
| `--project`                         | `project`                    | Path to .csproj or directory containing one.                   |
| `--configuration`                   | (no config)                  | Build configuration to use for packaging.                      |
| `--define`                          | (no config)                  | One or more preprocessor symbols to define.                    |
| `--no-restore`                      | (no config)                  | Skip the restore phase.                                        |
| `--property`                        | (no config)                  | Additional MSBuild properties in the form `name=value`.        |
| `--output-dir`                      | `outputDir`                  | Output directory (default: `./output`).                        |
| `--cache-dir`                       | `cacheDir`                   | Cache directory (default: `./cache`).                          |
| `--temp-dir`                        | `tempDir`                    | Temporary working directory (default: `./temp`).               |
| `--runtimes-version`                | `runtimesVersion`            | Version of runtime assets for RID packages.                    |
| `--runtimes-url`                    | `runtimesUrl`                | URL/format string for runtime archives.                        |
| `--force-runtimes-download`         | (no config)                  | Re-download the runtimes archive even if it is already cached. |
| `--runtimes-license-spdx`           | `runtimesLicenseSpdx`        | SPDX license expression for RID packages.                      |
| `--runtimes-license-file-url`       | `runtimesLicenseFileUrl`     | URL/format string to license file for RID packages.            |
| `--runtimes-license-spdx-file-url`  | `runtimesLicenseSpdxFileUrl` | URL/format string to text file with SPDX identifier.           |
| `--targets`                         | (no config)                  | Flavors to pack: core, meta, specific RIDs, or all.            |
| `--strict`                          | (no config)                  | Fail if a requested RID is missing.                            |
| `--no-symbols`                      | (no config)                  | Skip symbols package for core.                                 |
| `--verbose`                         | (no config)                  | Enable verbose logging.                                        |
| `--no-logo`                         | `noLogo`                     | Suppress startup banner.                                       |
| `--logo-file`                       | `logoFile`                   | Path to a text file containing startup logo ASCII art.         |

### push

Pushes packages to a NuGet feed. May invoke `pack` if needed.  
**Note:** `--api-key` is required.

```shell
./make.sh push --api-key YOUR_API_KEY
```

| CLI option       | Config property | Description                                                      |
|------------------|-----------------|------------------------------------------------------------------|
| `--api-key`      | (no config)     | Required. NuGet API key (never stored in config).                |
| `--nuget-source` | `nugetSource`   | NuGet feed URL (default: <https://api.nuget.org/v3/index.json>). |
| `--no-pack`      | (no config)     | Skip packing even if cache is stale.                             |
| `--fail-stale`   | (no config)     | Fail if cache is stale instead of packing.                       |

*All `pack` options are also accepted, since `push` may need to call `pack` before pushing.*

That includes `--strict` when `push` ends up running `pack` first.

### clean

Runs `dotnet clean` for the managed project and removes output, cache, and temp directories. If `--project` is omitted, the tool looks for the first `.csproj` inside `./src`.

```shell
./make.sh clean
```

| CLI option         | Config property | Description                                              |
|--------------------|-----------------|----------------------------------------------------------|
| `--project`        | `project`       | Path to .csproj or directory containing one.             |
| `--configuration`  | (no config)     | Build configuration to use for `dotnet clean`.           |
| `--no-restore`     | (no config)     | Skip the restore phase.                                  |
| `--property`       | (no config)     | Additional MSBuild properties in the form `name=value`.  |
| `--output-dir`     | `outputDir`     | Output directory to remove (default: `./output`).        |
| `--cache-dir`      | `cacheDir`      | Cache directory to remove (default: `./cache`).          |
| `--temp-dir`       | `tempDir`       | Temporary directory to remove (default: `./temp`).       |
| `--verbose`        | (no config)     | Enable verbose logging.                                  |
| `--no-logo`        | `noLogo`        | Suppress startup banner.                                 |
| `--logo-file`      | `logoFile`      | Path to a text file containing startup logo ASCII art.   |

### tests

Builds and runs discovered test projects with the configured native runtimes. If `--tests` is omitted, the tool searches `./tests` recursively.

```shell
./make.sh tests
```

| CLI option         | Config property | Description                                                                       |
|--------------------|-----------------|-----------------------------------------------------------------------------------|
| `--tests`          | `tests`         | One or more test projects or directories to search recursively for test projects. |
| `--configuration`  | (no config)     | Build configuration to use.                                                       |
| `--define`         | (no config)     | One or more preprocessor symbols to define.                                       |
| `--no-restore`     | (no config)     | Skip the restore phase.                                                           |
| `--property`       | (no config)     | Additional MSBuild properties in the form `name=value`.                           |
| `--verbose`        | (no config)     | Enable verbose logging.                                                           |
| `--no-logo`        | `noLogo`        | Suppress startup banner.                                                          |
| `--logo-file`      | `logoFile`      | Path to a text file containing startup logo ASCII art.                            |

The following options are forwarded to `dotnet test`:

| CLI option                         | Config property | Description                                      |
|------------------------------------|-----------------|--------------------------------------------------|
| `--environment`                    | (no config)     | Set environment variables for the test process.  |
| `--filter`                         | (no config)     | Select which tests to run.                       |
| `--logger`                         | (no config)     | Specify a test logger.                           |
| `--diag`                           | (no config)     | Write diagnostic logs to a file.                 |
| `--results-directory`              | (no config)     | Directory where test results are written.        |
| `--collect`                        | (no config)     | Enable one or more data collectors.              |
| `--blame`                          | (no config)     | Run tests in blame mode.                         |
| `--blame-crash`                    | (no config)     | Run tests in blame mode and collect crash dumps. |
| `--blame-crash-dump-type`          | (no config)     | Crash dump type to collect in blame crash mode.  |
| `--blame-crash-collect-always`     | (no config)     | Always collect crash dumps in blame crash mode.  |
| `--blame-hang`                     | (no config)     | Run tests in blame mode and collect hang dumps.  |
| `--blame-hang-dump-type`           | (no config)     | Hang dump type to collect in blame hang mode.    |
| `--blame-hang-timeout`             | (no config)     | Timeout to use in blame hang mode.               |

#### tests clean

Cleans the discovered test projects.

```shell
./make.sh tests clean
```

| CLI option         | Config property | Description                                                                       |
|--------------------|-----------------|-----------------------------------------------------------------------------------|
| `--tests`          | `tests`         | One or more test projects or directories to search recursively for test projects. |
| `--configuration`  | (no config)     | Build configuration to use for `dotnet clean`.                                    |
| `--no-restore`     | (no config)     | Skip the restore phase.                                                           |
| `--property`       | (no config)     | Additional MSBuild properties in the form `name=value`.                           |
| `--verbose`        | (no config)     | Enable verbose logging.                                                           |
| `--no-logo`        | `noLogo`        | Suppress startup banner.                                                          |
| `--logo-file`      | `logoFile`      | Path to a text file containing startup logo ASCII art.                            |

#### tests list

Lists the discovered test projects.

```shell
./make.sh tests list
```

| CLI option    | Config property | Description                                                                       |
|---------------|-----------------|-----------------------------------------------------------------------------------|
| `--tests`     | `tests`         | One or more test projects or directories to search recursively for test projects. |
| `--verbose`   | (no config)     | Enable verbose logging.                                                           |
| `--no-logo`   | `noLogo`        | Suppress startup banner.                                                          |
| `--logo-file` | `logoFile`      | Path to a text file containing startup logo ASCII art.                            |

### docs

Builds project documentation using [DocFX](https://dotnet.github.io/docfx/). If `--docfx` is omitted, the tool looks for `docfx.json` inside `./docs`.

> [!IMPORTANT]
> `make.cs docs` requires DocFX to be installed as a local `dotnet` tool.
> Example:
>
> ```shell
> dotnet tool install docfx --local
> ```

```shell
./make.sh docs
```

| CLI option                     | Config property           | Description                                                          |
|--------------------------------|---------------------------|----------------------------------------------------------------------|
| `--docfx`                      | `docfx`                   | Path to a `docfx.json` file or a directory containing one.           |
| `--docs-api-output`            | `docsApiOutput`           | Override the output directory passed to `docfx metadata`.            |
| `--docs-output`                | `docsOutput`              | Override the output directory passed to `docfx build`.               |
| `--without-metadata`           | (no config)               | Skip `docfx metadata`.                                               |
| `--without-build`              | (no config)               | Skip `docfx build`.                                                  |
| `--build-before-docs`          | `buildBeforeDocs`         | Build the managed project before running DocFX.                      |
| `--require-docfx-min-version`  | `requireDocfxMinVersion`  | Fail if the installed DocFX version is older than this version.      |
| `--project`                    | `project`                 | Project to build when `--build-before-docs` is used.                 |
| `--configuration`              | (no config)               | Build configuration to use when `--build-before-docs` is used.       |
| `--define`                     | (no config)               | One or more preprocessor symbols to define for the pre-docs build.   |
| `--no-restore`                 | (no config)               | Skip the restore phase for the pre-docs build.                       |
| `--property`                   | (no config)               | Additional MSBuild properties in the form `name=value`.              |
| `--verbose`                    | (no config)               | Enable verbose logging.                                              |
| `--no-logo`                    | `noLogo`                  | Suppress startup banner.                                             |
| `--logo-file`                  | `logoFile`                | Path to a text file containing startup logo ASCII art.               |

#### docs clean

Cleans documentation output directories. If `--docs-api-output` or `--docs-output` are omitted, the tool tries to resolve them from `docfx.json`.

```shell
./make.sh docs clean
```

| CLI option           | Config property | Description                                                |
|----------------------|-----------------|------------------------------------------------------------|
| `--docfx`            | `docfx`         | Path to a `docfx.json` file or a directory containing one. |
| `--docs-api-output`  | `docsApiOutput` | API documentation output directory to remove.              |
| `--docs-output`      | `docsOutput`    | Documentation output directory to remove.                  |
| `--verbose`          | (no config)     | Enable verbose logging.                                    |
| `--no-logo`          | `noLogo`        | Suppress startup banner.                                   |
| `--logo-file`        | `logoFile`      | Path to a text file containing startup logo ASCII art.     |

### ncover

Runs the bundled [ncover.cs](https://github.com/Sdl3Sharp/ncover.cs) tool against the managed project and the downloaded native runtime.

> [!IMPORTANT]
> The `ncover.cs` checkout must be present in the `make.cs/ncover.cs` directory.
> If you cloned the repository without submodules, initialize them with:
>
> ```shell
> git submodule update --init --recursive
> ```

```shell
./make.sh ncover
```

| CLI option                  | Config property       | Description                                                          |
|-----------------------------|-----------------------|----------------------------------------------------------------------|
| `--project`                 | `project`             | Path to .csproj or directory containing one.                         |
| `--configuration`           | (no config)           | Build configuration to use (default: `Debug`).                       |
| `--define`                  | (no config)           | One or more preprocessor symbols to define.                          |
| `--no-restore`              | (no config)           | Skip the restore phase.                                              |
| `--property`                | (no config)           | Additional MSBuild properties in the form `name=value`.              |
| `--verbose`                 | (no config)           | Enable verbose logging.                                              |
| `--no-logo`                 | `noLogo`              | Suppress startup banner.                                             |
| `--logo-file`               | `logoFile`            | Path to a text file containing startup logo ASCII art.               |
| `--runtimes-version`        | `runtimesVersion`     | Version of the runtimes package to download.                         |
| `--runtimes-url`            | `runtimesUrl`         | URL/format string for the runtimes archive.                          |
| `--force-runtimes-download` | (no config)           | Re-download the runtimes archive even if it is already cached.       |

The following options are forwarded to `ncover.cs`:

| CLI option          | Config property       | Description                                                             |
|---------------------|-----------------------|-------------------------------------------------------------------------|
| `--exclude-file`    | `nCoverExcludeFile`   | Path to a file containing symbol names to exclude, one per line.        |
| `--exclude`         | `nCoverExclude`       | Symbol names to exclude. Can be specified multiple times.               |
| `--min-severity`    | (no config)           | Minimum severity level to include in the report.                        |
| `--slight-as-warn`  | (no config)           | Treat slight issues as warnings.                                        |
| `--warn-as-error`   | (no config)           | Treat warnings as errors.                                               |
| `--json-output`     | (no config)           | Write an additional JSON report to a file.                              |
| `--no-unicode`      | (no config)           | Use ASCII symbols instead of Unicode in standard output.                |
| `--no-ansi`         | (no config)           | Disable ANSI escape codes in standard output.                           |
| `--verbosity`       | (no config)           | Output verbosity for the forwarded `ncover.cs` run.                     |
| `--pretty`          | (no config)           | Pretty-print JSON output when JSON output is enabled.                   |
| `--ncover-help`     | (no config)           | Show help for the bundled `ncover.cs` tool instead of running analysis. |

---

**Note:** Where a command option can also be supplied via `make.json`, the corresponding config property is listed in the tables above. CLI flags always take precedence. An example `make.json` is included in the repository; `_notes` entries are just documentation and ignored by the tool.

## How packing works

During `pack`, the tool generates temporary `.csproj` files and defines two MSBuild properties:

- **`MakeFlavor`**: Indicates the package flavor (`core`, `rid`, or `meta`).  
- **`MakeFlavorRid`**: For RID‑specific packages, set to the RID (e.g. `win-x64`). Not set for core or meta.

**Flavors:**

- **Core**: `MakeFlavor=core`  
- **RID‑specific**: `MakeFlavor=rid`, `MakeFlavorRid=<RID>`; includes native binary under `runtimes/{RID}/native`; license metadata from SPDX or license file options.  
- **Meta**: `MakeFlavor=meta`; depends on core and/or RID packages.

**Customizing builds:**

- `Directory.Build.props` can set defaults but is imported too early to see `MakeFlavor` or `MakeFlavorRid`.  
- `Directory.Build.targets` is imported later and can use these properties for flavor‑ or RID‑specific logic.

Example in `Directory.Build.targets`:

```xml
<Target Name="CustomRidPostPack" AfterTargets="Pack" Condition="'$(MakeFlavor)' == 'rid'">
  <!-- Custom logic for RID-specific packages -->
</Target>

<PropertyGroup Condition="'$(MakeFlavorRid)' == 'linux-x64'">
  <!-- Linux-specific tweaks -->
</PropertyGroup>
```

## License

Licensed under the MIT License. See [LICENSE.md](LICENSE.md) for details.
