---
name: Validation and Run button
description: Prevent validation registration from replacing the browser preview launcher.
---

Configure the Project Run-button workflow after registering validation commands.

**Why:** Registering a TypeScript validation recreated Project with only the short-lived check, overriding the previously selected web launcher. A separately running artifact masked this until the Run-button configuration was inspected.

**How to apply:** After validation registration, inspect Project's tasks and use validated platform configuration to point it at the existing managed web workflow. Restart Project itself to verify Run, not just the artifact independently.

A failed managed startup can coexist with a working preview when a previous server process still owns the service port.

**Why:** A managed restart reported a port conflict while an older Vite process continued serving the game; an HTTP success alone did not establish that the managed workflow had recovered.

**How to apply:** Identify the actual listener and its command before restarting again. Stop only the confirmed stale process, then restart the existing registered service and check its managed state. Do not launch a replacement workflow or indiscriminately kill unrelated servers.

Prefer a direct, exec-based web-server launch over a wrapper that invokes another package-manager script solely to start the same service.

**Why:** The port conflict recurred after workspace reactivation with an earlier nested package-manager/Vite chain still alive. Removing the unnecessary wrapper makes process ownership clearer; it does not guarantee that a development URL remains online during workspace inactivity.

**How to apply:** Preserve the service's injected environment and working directory, reduce unnecessary launch layers, and verify both the registered workflow's state and the external game page. Never describe restarting a development server as a permanent hosting solution.
