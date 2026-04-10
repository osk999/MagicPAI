# AGENTS.md

## Open Source Reference Policy
- When working on Elsa-related behavior, APIs, patterns, Studio integration, activities, workflow runtime, persistence, descriptors, designer behavior, or debugging, check `document_refernce_opensource/` before making assumptions.
- Treat `document_refernce_opensource/` as the repo-local reference source for upstream Elsa docs, `elsa-core`, and `elsa-studio`.
- Prefer the local reference folder over memory when explaining framework behavior.
- Treat the folder as a snapshot, not guaranteed-current upstream truth. If exact version alignment matters, say so explicitly.

## Lookup Order
- Start with `document_refernce_opensource/README.md`.
- Use `document_refernce_opensource/REFERENCE_INDEX.md` to choose the narrowest relevant area.
- Use `document_refernce_opensource/docs/` first for expected behavior and concepts.
- Use `document_refernce_opensource/elsa-core/` for runtime, server, persistence, workflow execution, and backend implementation details.
- Use `document_refernce_opensource/elsa-studio/` for Studio UI, designer, client behavior, and frontend implementation details.
- Read only the specific files needed. Do not load large unrelated trees into context.

## Troubleshooting Workflow
- Check MagicPAI code first to locate the actual failing integration point.
- Verify expected Elsa behavior in `document_refernce_opensource/docs/`.
- If docs are incomplete or ambiguous, inspect the matching upstream source in `elsa-core` or `elsa-studio`.
- In explanations, clearly separate:
  - MagicPAI code behavior
  - Elsa upstream behavior
  - inference from reading source
- If MagicPAI behavior conflicts with upstream docs or source, call that out explicitly instead of guessing.

## Context Hygiene
- Do not scan the entire reference folder by default.
- Prefer targeted file reads and `rg` searches.
- Summarize upstream behavior instead of pasting large code blocks.
- Mention the exact local reference file path used when it materially supports the explanation.

## Editing Policy
- Do not modify `document_refernce_opensource/` unless the task is specifically to refresh, reorganize, or document that snapshot.
- Do not copy upstream Elsa code into MagicPAI unless there is a clear reason.
- Keep fixes aligned with upstream Elsa patterns unless MagicPAI intentionally diverges.
