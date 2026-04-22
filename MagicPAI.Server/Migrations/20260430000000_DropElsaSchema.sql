-- MagicPAI.Server/Migrations/20260430000000_DropElsaSchema.sql
-- Phase 3 Day 13 — drops every Elsa-owned table from the MagicPAI database.
-- Per temporal.md §K.2. Safe to run against any environment that previously
-- hosted Elsa; if no Elsa tables exist the DROPs become no-ops.
--
-- MagicPAI.Server no longer owns a DbContext after Elsa retirement, so this
-- lives as a raw SQL script. Apply against production PostgreSQL via:
--
--   psql -U magicpai -d magicpai -f 20260430000000_DropElsaSchema.sql
--
-- For SQLite dev databases use the same statements without the CASCADE
-- keyword (SQLite drops dependent objects implicitly).

DROP TABLE IF EXISTS "WorkflowDefinitions" CASCADE;
DROP TABLE IF EXISTS "WorkflowDefinitionPublishers" CASCADE;
DROP TABLE IF EXISTS "WorkflowInstances" CASCADE;
DROP TABLE IF EXISTS "WorkflowExecutionLogs" CASCADE;
DROP TABLE IF EXISTS "ActivityExecutions" CASCADE;
DROP TABLE IF EXISTS "Bookmarks" CASCADE;
DROP TABLE IF EXISTS "Triggers" CASCADE;
DROP TABLE IF EXISTS "Stimulus" CASCADE;
DROP TABLE IF EXISTS "KeyValues" CASCADE;
DROP TABLE IF EXISTS "SerializedPayloads" CASCADE;
