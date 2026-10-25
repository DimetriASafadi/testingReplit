---
name: Android build environment
description: SDK persistence and safe update signing when rebuilding offline test APKs.
---

Do not assume an Android SDK installed under /tmp survives a later session. Keep reusable build tools in a persistent ignored, non-deployed directory and verify the SDK exists before starting a native build.

**Why:** A later APK rebuild failed because the previously working temporary SDK directory was gone, although prior APKs and source files remained.

**How to apply:** Check the current environment and documented SDK setup first; the current reusable installation is under generated-artifacts/android-sdk, excluded from Git and deployment. Build tools are not application assets.

Do not conclude a debug signing key is lost solely because the conventional home-directory location is empty.

**Why:** This environment retained the existing key in an XDG configuration location. Its APK signing certificate still matched the prior distributed APK.

**How to apply:** Compare the new and previous APK certificate fingerprints before claiming an in-place update is supported. Never replace signing identity or recommend uninstalling without addressing local save loss. Never copy key contents into memory or documentation.
