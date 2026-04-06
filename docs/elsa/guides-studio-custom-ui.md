# Custom UI Components for Elsa Studio

## Overview

Elsa Studio enables developers to create specialized property editors beyond the default options. You can customize Property Editors (custom input controls for activity properties) as well as content visualizers and activity pickers.

## How Editors Are Rendered

The system follows a five-step process: it loads activity metadata, determines property types, selects appropriate UI components based on data type and UI hints, renders the property panel, and binds data to workflow definitions.

## UIHint Attribute Usage

Developers specify custom editors through the `UIHint` attribute. For example, an activity might use `UIHint = "email-input"` to request a specialized email input component rather than a generic text field.

## Creating Custom Editors

Custom property editors require both backend and frontend components:

**Backend**: Activities use `UIHint` attributes, with optional UI hint handlers for metadata and validation.

**Frontend**: Web components must implement the property editor interface, which includes properties for `value`, `isExpression`, and `propertyDescriptor`, plus a `valueChanged` event.

## Framework Integration

The documentation provides patterns for integrating React and Angular components by wrapping them as web components. This approach allows developers to leverage existing component libraries while maintaining compatibility with Studio's architecture.

## Key Best Practices

Editors should handle expression mode (where properties contain code rather than literal values), emit `valueChanged` events, provide input validation, maintain responsive design for narrow panels, and include accessibility features like ARIA labels.
