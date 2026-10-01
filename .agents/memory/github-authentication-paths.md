---
name: GitHub authentication paths
description: Delivering changes safely when the GitHub connector works but native Git credentials do not.
---

GitHub connector authorization and the workspace's native Git authentication are separate paths. A working connector does not prove that command-line pushes are authenticated.

**Why:** Native Git rejected authentication while the authorized connector successfully accessed the same repository with write permissions. The imported local branch also lacked some remote history despite a clean working tree.

**How to apply:** If native push fails, use the authenticated connector proxy rather than extracting tokens or adding credentials to remote URLs. Check the actual remote head and integrate existing remote changes without force-pushing. When transferring Git objects through the API, preserve and verify their original hashes so local and remote histories remain compatible; only update the branch using a fast-forward operation.

Treat the local commit head as live state between tool calls, not a fixed session baseline.

**Why:** Replit checkpoints can commit pending changes while the agent is working, so files that were just marked for deletion may already be committed by the next operation.

**How to apply:** Check status before staging, freeze the head for each transfer, and verify it again before updating the remote branch. Do not recreate deleted files or rewrite history merely because an earlier staging assumption became stale.