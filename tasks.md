# Implementation Tasks

## Phase 1: Safety & Reliability

1. JSON code-fence stripping in TenantLlmWorkflowAgent
2. Fix Anthropic max_tokens hardcode
3. Fix unpooled HttpClient
4. Enrich AgentInsightGenerated domain event
5. API key encryption at rest
6. E2E agent automation test

## Phase 2: Context & Robustness

7. Context window / token budgeting
8. Provider-specific error handling (Anthropic overloaded, Gemini SAFETY)
9. AgentTaskProcessorService integration tests
10. Provider-specific adapter tests

## Phase 3: Advanced AI Capabilities

11. Native LLM tool calling (ReAct loop)
12. Agent observability dashboard

## Phase 4: Agentic Patterns

13. Provider fallback chains
14. Multi-turn conversational agents
15. Dynamic provider registry (DI-based)
16. Multi-action agent responses
17. PostgreSQL concurrency tests (Testcontainers)
18. Raw prompt/response audit log (opt-in)
