# Source-only Git checkout

This repository tracks the game's code and configuration, not the heavy local Unity asset library.

## Tracked
- C# source and tests, shaders, scenes and project/package settings.
- Runtime geographic JSON and small asset manifests/licenses.
- Asset-generation tools and project documentation.

## Local only
- Models, textures, audio and fonts, including their asset metadata.
- Uploaded references and downloaded binary inputs.
- Generated exports, full-project ZIPs and split ZIP parts.
- Unity and .NET caches/build output.

## Moving from the old Git history

The repository history was cleaned with approval. Old commit IDs changed; do not merge the old history back into the cleaned branch.

1. Keep the complete backed-up PC Unity project, including its `Assets` folder and `.meta` files.
2. Clone `https://github.com/DimetriASafadi/testingReplit.git` into a **new folder**.
3. Copy the local model/texture/audio/font assets and their matching `.meta` files from the backup into the same paths in the new project's `testingReplic/Assets`. Restore any locally needed font-authoring or test-fixture inputs as well.
4. Open `testingReplic` in Unity on the PC and test normally.

Do not copy the backup's `.git`, generated export ZIPs, `Library`, `Temp`, `bin` or `obj` folders into the new clone. Keep the old backup separate until the new checkout is working.

Normal updates after that are ordinary Git pulls. Large local assets are ignored and must not be force-added. Optional authoring/integration tests that consume generated exports need those fixtures regenerated or restored locally; the Unity game does not load `exports/`.
