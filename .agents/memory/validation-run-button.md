---
name: Validation and Run button
description: Prevent validation registration from replacing the browser preview launcher.
---

Configure the Project Run-button workflow after registering validation commands.

**Why:** Registering a TypeScript validation recreated Project with only the short-lived check, overriding the previously selected web launcher. A separately running artifact masked this until the Run-button configuration was inspected.

**How to apply:** After validation registration, inspect Project's tasks and use validated platform configuration to point it at the existing managed web workflow. Restart Project itself to verify Run, not just the artifact independently.
