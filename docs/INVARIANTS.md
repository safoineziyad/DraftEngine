1. Template generation works fully offline. AI is optional, never required.
2. Sensitive data stays DPAPI-encrypted at rest. Never log contract text, intake answers, chat messages, API keys, or AI request/response bodies.
3. Version snapshot before any destructive change (AI edit, regenerate, revert, edit-and-regenerate).
4. Page ViewModels are singletons: reload state on every navigation.
5. Exporters depend on the "N. Title" heading convention. Do not break it.
6. Any seed change needs an EF migration.
7. CurrentUserId = 1 remains the single-user assumption.
8. All existing tests keep passing; every new feature ships with tests.
9. No telemetry. Network calls only: the OpenAI/user-configured AI endpoint and the signed update-manifest check. Update checks are **on by default** (notify-only: one HTTPS GET with `User-Agent: DraftEngine/<version>`, no identifiers, no query string), can be turned off in Settings (then no update request is made at all), and are disclosed in the first-run screen and About privacy text. Installing an update always requires an explicit click plus a confirmation.
10. Do not rebuild anything that already works.
