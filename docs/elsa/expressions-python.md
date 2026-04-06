# Python Integration Guide

## Installation Overview

The documentation provides platform-specific instructions for installing Python before enabling Python expressions in workflows.

### Windows Setup

Users must download the official Python installer, run it with "Add Python to PATH" enabled, and verify installation via Command Prompt using `python --version`. The guide emphasizes locating the `python38.dll` file path using the `where python` command, as this shared library path will be needed for configuration.

### macOS Setup

On macOS, users download the 64-bit installer package, complete the installation process, and verify it using Terminal with `python3 --version`. The shared library file (`libpython3.8.dylib`) is typically found in the `lib` directory within the Python installation path.

### Environment Configuration

Users must configure the `PYTHONNET_PYDLL` environment variable to reference the Python DLL location and set `PYTHONNET_RUNTIME` to `coreclr`. The documentation references the Pythonnet GitHub project for additional details.

## Feature Installation

To enable Python expressions, developers add the `Elsa.Python` package and call `UsePython()` in their service configuration. The feature supports optional configuration through delegates, allowing developers to add custom Python scripts that initialize before expression evaluation.

## Available Globals

Four primary objects are available in Python expressions:

- **output**: Accesses activity results via `get()` or retrieves the last execution's output
- **input**: Retrieves workflow input data using `get()`
- **variables**: Manages workflow variables through property assignment or `set()`/`get()` methods
- **execution_context**: Provides `workflow_instance_id` and `correlation_id` properties
