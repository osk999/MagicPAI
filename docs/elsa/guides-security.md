# Security & Authentication Guide

## Core Identity Configuration

The guide emphasizes implementing `UseIdentity` and `UseDefaultAuthentication` in Elsa Server's `Program.cs`. This setup supports API keys, JWT tokens, and OIDC integration, with configuration loaded from `appsettings.json`.

Token lifetime recommendations include access tokens: 1 hour, refresh tokens: 7 days, though these should be adjusted based on organizational security policies.

## Authentication Approach Selection

The guide recommends preferring JWT/OIDC for user-facing endpoints and reserving API keys for trusted service-to-service communication. This distinction helps balance security with operational simplicity.

API keys require rotation every 90 days or per your security policy, and sensitive values must never be committed to version control.

## Bookmark Token Security

Resume tokens are encrypted and contain bookmark metadata, with security controls including:

- Short-lived bookmarks for webhooks (minutes) versus longer durations for email approvals
- Automatic invalidation after consumption via "AutoBurn" functionality
- Audit logging of all resume attempts with source IP and context
- Rate limiting recommendations of 100 requests/minute per IP

## Network & Deployment Hardening

The guide requires TLS 1.2 or higher for all HTTP endpoints with valid, trusted certificates. For multi-node deployments, the documentation clarifies that sticky sessions are not required for workflow runtime since Elsa uses distributed locking and database persistence.

CORS policies must never use `AllowAnyOrigin()` in production; instead, explicitly whitelist necessary origins.

## Production Verification

A comprehensive checklist covers identity configuration, network security, secrets management, logging practices, clustering requirements, and infrastructure controls. The guide emphasizes that secrets must be stored in secret managers rather than environment files or source control.
