All workflow definitions are JSON-backed templates loaded by `WorkflowPublisher`,
except `FullOrchestrateWorkflow` and `WebsiteAuditLoopWorkflow` which remain
code-first because their `ExecuteWorkflow` activities require delegate-based
`Input<IDictionary<string, object>>` expressions for child workflow input building.

Delegate-based `Input<T>(ctx => ...)` expressions in all other workflows have been
replaced with `JavaScript` expression types (evaluated by Jint) or `Variable` references.

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
- `website-audit-core.json`

Code-first only (2):
- `FullOrchestrateWorkflow`
- `WebsiteAuditLoopWorkflow`
