# Upgrade Options — cya2.0

Assessment: 6 SDK-style projects; one Blazor web project on net10.0 and five supporting/test projects on net8.0; 3 source-incompatible API findings.

## Strategy

### Upgrade Strategy
The solution is a small modern-.NET solution with a shallow dependency structure and no incompatible packages, so an atomic upgrade avoids unnecessary multi-targeting overhead.

| Value | Description |
|-------|-------------|
| **All-at-Once** (selected) | Upgrade all projects simultaneously in a single atomic pass. |
| Top-Down | Upgrade entry-point applications first and temporarily multi-target shared libraries, then consolidate them. |

## Compatibility

### Unsupported API Handling
The assessment identified three source-incompatible BCL API findings; these are minor modern-to-modern changes and should be resolved directly.

| Value | Description |
|-------|-------------|
| **Fix Inline** (selected) | Resolve every API change in the same task, including complex ones. |
| Defer Complex Changes | Apply simple replacements inline and create stubs plus follow-up work for complex changes. |
