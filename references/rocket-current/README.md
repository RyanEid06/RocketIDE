# Rocket tooling reference snapshot

This directory is a read-only design snapshot copied from the Rocket repository version supplied when the RocketIDE plan was created on 2026-09-10.

Purpose: give RocketIDE implementers direct access to the current language-server/compiler/editor contracts without guessing.

Do not modify these files to change RocketIDE behavior. If Rocket evolves, refresh the snapshot from the authoritative Rocket repository and review integration changes deliberately.

The selected C# Visual Studio sources are **reference implementations of concepts**, not code to copy blindly. They depend on Visual Studio APIs or older framework facilities in places. Port the behavior into RocketIDE's .NET 10 architecture with tests.
