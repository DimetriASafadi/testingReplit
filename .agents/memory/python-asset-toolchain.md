---
name: Python asset toolchain
description: Avoid native Pillow import failures when running the project's image-processing tools.
---

Run Pillow-backed asset tools with a Python version matching the installed native modules. In this workspace, bare `python` may resolve to Python 3.12 while `.pythonlibs` contains Python 3.13 wheels.

**Why:** The shell's default interpreter produced a Pillow `_imaging` import error, while the installed Python 3.13 executable loaded Pillow and NumPy successfully.

**How to apply:** Check the interpreter version before reinstalling packages; use the matching versioned executable for image packing and other native Python tooling.
