# Testing & Debugging Workflows in Elsa V3

## Overview

This comprehensive guide covers strategies for testing workflows with xUnit and Elsa.Testing, integration testing patterns, debugging techniques, and best practices for production-ready workflow systems.

## Why Test Workflows?

As the documentation states, testing workflows provides several critical benefits: **"Ensure workflows behave correctly before deployment"** along with regression prevention, executable documentation, safe refactoring, and comprehensive quality assurance for business rules and edge cases.

## Unit Testing Workflows

### Setup Process

The recommended approach involves creating an xUnit test project with necessary packages:
- Elsa
- Elsa.Testing.Shared
- Microsoft.Extensions.DependencyInjection
- xUnit and visualization tools

A base test class should implement `IAsyncLifetime` to establish the Elsa service container with proper registry population.

### Testing Simple Workflows

Unit tests should follow the Arrange-Act-Assert pattern, validating workflow status and output. The documentation provides examples testing email validation logic with valid and invalid inputs using `SetVariable`, `If`, and `WriteLine` activities.

### Testing Custom Activities

Custom activities should be tested in isolation by wrapping them in a minimal workflow context. The examples demonstrate testing a `SendEmailActivity` that validates recipient addresses and tracks execution.

### Official Testing Helpers

**ActivityTestFixture** from `Elsa.Testing.Shared` is the recommended approach for unit testing individual activities in isolation.

**WorkflowTestFixture** from `Elsa.Testing.Shared.Integration` provides complete test infrastructure with proper service setup, supporting both synchronous and asynchronous workflow execution.

**AsyncWorkflowRunner** handles workflows with timers or external triggers, properly awaiting completion signals for deterministic testing.

## Integration Testing

### TestContainers Approach

Integration tests should run real dependencies in Docker containers. The guide provides examples using PostgreSQL with proper connection string management and database migrations.

### In-Memory Databases

For faster testing cycles, Entity Framework Core's in-memory database provider eliminates container overhead while maintaining realistic persistence testing.

### HTTP Workflow Testing

ASP.NET Core's TestServer enables testing HTTP-triggered workflows without external network calls, supporting POST requests with JSON payloads and response validation.

## Debugging Workflows

### Execution Journal

Workflows generate execution logs recording every activity transition, providing an **"complete audit trail"** for analyzing execution flow and identifying failure points.

### Logging Configuration

Structured logging via Serilog enables comprehensive debugging with configurable levels and output targets like console and rolling files.

### WriteLine for Tracing

Strategic `WriteLine` activities inserted throughout workflows provide runtime visibility into variable values and execution progression.

### Breakpoint Activity

Custom breakpoint activities pause execution and display workflow state including variables and activity context, useful for interactive debugging.

### State Inspection

The `IWorkflowStateStore` service allows accessing persisted workflow state, including variables, bookmarks, and scheduled activities after execution completes.

### Fault Debugging

Faulted workflows expose `Incidents` containing error messages and stack traces. The execution log reveals which activities executed before the fault occurred.

When testing faults, **"prefer using _fixture.GetActivityStatus(result, activity) to check if a specific activity faulted"** rather than only checking workflow-level status.

## Test Data Management

### Builder Pattern

Test data builders create complex objects fluently:
```csharp
var workflow = new TestWorkflowBuilder()
    .WithDefinitionId("test-workflow-1")
    .AddWriteLine("Starting")
    .Build();
```

### Test Fixtures

xUnit class fixtures provide shared test infrastructure initialized once per test class, reducing setup overhead.

### Parameterized Tests

`Theory` and `InlineData` attributes enable data-driven testing across multiple input combinations using the same test logic.

## CI/CD Integration

### GitHub Actions

Tests should be categorized by trait (Unit, Integration, E2E) for selective execution. The pipeline can run unit tests quickly, then slower integration tests with database services.

### Azure DevOps

Similar organization with separate jobs for unit and integration tests, supporting code coverage collection and publishing.

### Test Categories

Tests marked with `[Trait("Category", "Unit")]` can be filtered during execution:
```
dotnet test --filter "Category=Unit"
```

## Common Pitfalls & Solutions

**Pitfall 1: Registry Population** - Always call `PopulateRegistriesAsync()` after building the service provider to prevent "Activity type not found" errors.

**Pitfall 2: Shared State** - Use `IAsyncLifetime` to ensure fresh service providers for each test, preventing cross-test contamination.

**Pitfall 3: Async Testing** - Use proper async/await patterns with cancellation tokens and timeouts rather than `Thread.Sleep()`.

**Pitfall 4: Edge Cases** - Test boundary conditions including null values, empty strings, and invalid formats.

**Pitfall 5: Resource Disposal** - Implement proper disposal patterns for service providers and test containers.

**Pitfall 6: Hardcoded Waits** - Replace fixed delays with polling mechanisms that check workflow completion status.

## Best Practices

1. **Test Pyramid**: Many unit tests (fast, isolated), some integration tests, few end-to-end tests

2. **Arrange-Act-Assert**: Structure tests with clear setup, execution, and verification phases

3. **Descriptive Names**: Test names should describe behavior being tested and expected outcomes

4. **Single Responsibility**: Each test verifies one specific behavior, not multiple scenarios

5. **Mock Dependencies**: Isolate workflows from external systems in unit tests using Moq

6. **Reusable Helpers**: Create test utilities for common operations like workflow creation and output extraction

7. **Error Handling**: Explicitly test failure scenarios and exception handling paths

8. **Speed Optimization**: Use in-memory databases, parallelize tests, and reserve containers for integration tests only

9. **Data Organization**: Store test workflows and data near tests in version control

10. **Documentation**: Comment complex test logic explaining business rules being verified

## Key Takeaway

The documentation emphasizes that **"Testing workflows provides confidence, prevents regressions, serves as documentation, enables safe refactoring, and validates business rules and edge cases."** By using official Elsa testing helpers (ActivityTestFixture, WorkflowTestFixture, AsyncWorkflowRunner) and following established patterns, developers can build reliable test suites supporting rapid iteration on workflow-based applications.
