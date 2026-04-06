# MassTransit Overview

MassTransit is described as "an open-source distributed application framework for .NET that provides a consistent abstraction on top of the supported message transports."

## Key Capabilities

The framework allows developers to model messages as C# types for sending and receiving. Traditional message handling involves writing Consumer classes, but the Elsa integration introduces an alternative approach through workflow activities.

## Workflow Integration with Elsa

When registering message types with MassTransit's Elsa module, the system automatically generates two activities:

1. **Publish {activity type}** - Publishes your message to the transport
2. **{activity type}** - Serves as a trigger to initiate or continue workflow execution upon receiving a matching message

This integration streamlines the process of incorporating messaging into .NET workflows without requiring manual Consumer implementation.
