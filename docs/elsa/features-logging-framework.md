# Logging Framework

The **Elsa.Logging** module enables flexible log capture and routing to multiple destinations through programmatic or configuration-based setup.

## Key Configuration Methods

You can establish logging in two primary ways:

1. **Programmatic approach**: Define sinks directly in `Program.cs` using `LoggerFactory.Create()` with console, file, or custom targets.

2. **Configuration-based approach**: Declare sinks in `appsettings.json` under the `LoggingFramework` section, specifying sink type, name, and options.

## Core Components

**Log Activity Properties:**
The workflow Log activity accepts message templates with placeholder substitution. It supports specifying the log level, category, target sinks, and additional attributes for structured logging.

**Filtering**: "Log sinks follow the same filtering semantics as the built-in ASP.NET Core logging system," allowing minimum levels and category-specific overrides.

## Custom Sink Implementation

Developers can extend the framework by implementing `ILogSinkFactory<TOptions>`. The process involves:
- Creating a factory class with required methods
- Registering it in dependency injection
- Referencing it in configuration with a custom type identifier

Built-in examples like `ConsoleLogSinkFactory` and `SerilogLogSinkFactory` serve as implementation references for creating reusable logging targets compatible with both code and configuration-based usage patterns.
