# BottomlessRack project instructions

Read the documentation relevant to the change and update it in the same change as the implementation.

- Use the [README](README.md) at the repository root for player-facing features, progression rules, and instructions. Update it when what players can do or expect changes.
- Use the [design notes](Docs/DESIGN.md) for behavior, implementation rationale, compatibility findings, and verification scenarios. Check the relevant section before changing a feature and revise it when its assumptions or behavior change.
- Use the [Harmony patch inventory](Docs/PATCHES.md) to find which EFT or SPT methods implement a feature. When adding, removing, retargeting, or changing a patch, update its entry, hook type, source link, and totals as applicable. Keep test-only hooks in their own section.

Before finishing a documentation change, check the affected descriptions against the current source and verify relative links from the file that contains them.
