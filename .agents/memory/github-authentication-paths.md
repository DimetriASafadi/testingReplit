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

Create Git tree objects in dependency order: children before parents.

**Why:** Reversing `git rev-list --objects` did not produce a topological tree order across multiple commits. GitHub rejected a parent containing a child tree that had not yet been created, although its blobs had uploaded correctly.

**How to apply:** Traverse the explicit tree dependencies recursively, then create commits parent-first. Preserve every object hash and advance the branch only after all dependencies exist; resume from uploaded objects rather than resending large archive blobs.

Large binary archives can exceed GitHub's blob API request-body limit before reaching the repository's regular file-size limit.

**Why:** A roughly 45 MB source ZIP was rejected with HTTP 422 “input was too large” while earlier smaller source archives uploaded successfully. Base64 further enlarges the request body.

**How to apply:** Inventory newly introduced blobs before API delivery, including archives captured by automatic checkpoints before ignore rules were added. Exclude oversized generated downloads from source-only delivery commits; preserve their local files and the original unpublished checkpoint under a backup ref. Never truncate assets, reduce art quality or rewrite already-published history.