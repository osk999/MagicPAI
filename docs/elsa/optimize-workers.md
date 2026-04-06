# Worker Count

## Configuration

To adjust the number of workers that Elsa uses internally, you can modify the `MediatorOptions` configuration setting. For instance, in your `Program.cs` file:

```csharp
builder.Services.Configure<MediatorOptions>(opt =>
{
    opt.CommandWorkerCount = 16;
    opt.JobWorkerCount = 16;
    opt.NotificationWorkerCount = 16;
});
```

This configuration allows you to set three distinct worker counts:
- **CommandWorkerCount**: Controls the number of command processing workers
- **JobWorkerCount**: Manages job execution workers
- **NotificationWorkerCount**: Handles notification delivery workers

Each can be independently tuned to match your application's performance requirements.
