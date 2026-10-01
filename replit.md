# Project context

- This is an imported native Unity 6 project, not a browser/React app. Unity project root: `testingReplic`.
- Product: **New Gaza / نيو غزة**, a mobile 3D reconstruction/economy game. Source requirements are the Arabic DOCX under `attached_assets`; working coverage and limitations are in `testingReplic/PROJECT.md`.
- Preserve the imported Unity/URP/Input System stack. Do not migrate the repository or create a web substitute merely to provide a preview.
- New game entry scene: `Assets/NewGaza/Scenes/NewGaza.unity`. Original racing prototype stays in `Assets/Scenes/SampleScene.unity`, disabled in the mobile build list.
- Gameplay is peaceful reconstruction, not combat: rubble → recycling → resources → revenue → infrastructure/projects → 100% neighborhood reward → next neighborhood → Al Rashid finale.
- Runtime-generated world; use the Unity menu `New Gaza/Open game scene`, then Play. The setup menu applies mobile settings and Input System configuration. See `testingReplic/README.ar.md`.
- Unity editor is not installed in this workspace. Never equate source/domain checks with a Unity compilation, play-mode run, device performance test, or store-ready build.
- Domain checks: `dotnet run --project testingReplic/Tests/Domain/DomainTests.csproj --configuration Release`.
- Source checks: `dotnet run --project testingReplic/Tests/SourceChecks/SourceChecks.csproj --configuration Release`.
- Keep `.meta` GUIDs with assets. A locally renamed Unity file in a user's stack trace may not exist in this workspace; distinguish copies instead of assuming an asset-cache problem.
- No paid ad simulation, no fabricated reward callbacks. Real rewarded ads, native sharing/video capture, cloud saves, and store signing are not configured.
- The current map is a stylized diorama, not verified geographic reconstruction. Later neighborhood order is provisional; do not imply official boundaries or reconstruction priorities.