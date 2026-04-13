Most workflow definitions in this folder are JSON-backed templates loaded by
`WorkflowPublisher`.

`FullOrchestrateWorkflow`, `WebsiteAuditCoreWorkflow`, and `WebsiteAuditLoopWorkflow`
remain code-first because the current template export path does not preserve their
delegate-built child input or prompt expressions correctly.

The C# workflow classes in `../` are retained as canonical sources and can be
used to regenerate these templates via `MagicPAI:WorkflowTemplates:RefreshFromCode=true`.

JSON-backed templates (16):
- `claw-eval-agent.json`
- `context-gatherer.json`
- `is-complex-app.json`
- `is-website-project.json`
- `loop-verifier.json`
- `orchestrate-complex-path.json`
- `orchestrate-simple-path.json`
- `post-execution-pipeline.json`
- `prompt-enhancer.json`
- `prompt-grounding.json`
- `research-pipeline.json`
- `simple-agent.json`
- `standard-orchestrate.json`
- `test-set-prompt.json`
- `verify-and-repair.json`

Code-first only (3):
- `FullOrchestrateWorkflow`
- `WebsiteAuditCoreWorkflow`
- `WebsiteAuditLoopWorkflow`
