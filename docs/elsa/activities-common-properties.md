# Common Properties

### Name
Activity names serve as references for accessing output properties in other activities via expressions. Names must follow JavaScript symbol conventions -- no spaces, dots, hyphens, or similar characters. See documentation on JavaScript and Liquid expressions for implementation details.

### Display Name
The Display Name appears in the designer interface when visualizing activities. Custom display names clarify an activity's purpose, such as labeling a "Write Line" activity as "Write Current Status" to indicate its function.

### Description
The Description property provides custom explanatory text displayed in an activity's designer body area. This enhances workflow comprehensibility. User-provided descriptions override default descriptions when specified.

### Load Workflow Context
This setting controls workflow context loading at the individual activity level. When enabled, it instructs the workflow runner to load the Workflow Context from the provider before the activity executes, ensuring a fresh copy in memory. Most workflows don't require this since context loads automatically.

### Save Workflow Context
This setting manages workflow context persistence at the activity level. When enabled, the workflow runner saves the Workflow Context to storage after activity execution, ensuring changes persist before subsequent activities run. Automatic saving typically handles this functionality.

### Save Workflow Instance
This property controls whether a workflow instance persists to storage after a specific activity completes. It's useful when you need to ensure persistence at critical points, particularly when workflows aren't configured to save after each execution burst.

### Storage
Many activities include Storage category properties controlling where input and output values persist. By default, values remain within the workflow instance. However, for large data -- like files from HTTP requests -- storing values as inline base64 strings can be inefficient. Alternative providers like Transient or Blob Storage offer better performance depending on access needs.
