1. Template generation works fully offline. AI is optional, never required.
2. Sensitive data stays DPAPI-encrypted at rest. Never log contract text, intake answers, chat messages, API keys, or AI request/response bodies.
3. Version snapshot before any destructive change (AI edit, regenerate, revert, edit-and-regenerate).
4. Page ViewModels are singletons: reload state on every navigation.
5. Exporters depend on the "N. Title" heading convention. Do not break it.
6. Any seed change needs an EF migration.
7. CurrentUserId = 1 remains the single-user assumption.
8. All existing tests keep passing; every new feature ships with tests.
9. No telemetry. Network calls only: OpenAI/user-configured AI endpoint and the GitHub update check (enabled by default, configurable off).
10. Do not rebuild anything that already works.
