# Grassland presentation art

- `grassland_expedition.png`: loading illustration generated with the built-in ImageGen tool on 2026-10-13. The original is kept at its generated resolution; Godot fits it to the 1280×720 viewport with nearest filtering. Text and progress remain separate UI nodes.
- `../../scripts/visuals/BattlefieldArt.cs`: deterministic, code-native pixel decoration for the existing terrain. Reads the grid's tile classification, draws sparse grass, dirt flecks, blocked-cell rocks and outer shrubs, and never owns collision or deployment rules. Cell interiors keep a 13-pixel decoration inset. Z-index 1 remains below units (3+) and projectiles.
- `../../scenes/loading_screen.tscn`: standalone preview scene and production overlay. The initial selector preloads the battlefield once per process; confirmed skill preparation uses the same overlay before entering combat. Progress comes from Godot's threaded resource loader. Visible runs retain it for at least 1.1 seconds; headless runs complete immediately when ready.

## Image generation prompt

Use case: illustration-story. Asset type: 16:9 loading screen illustration for a 1280x720 SD pixel-art tower defense game. Bright grassy ground, warm winding dirt path, distant small surface research outpost. A charming white cat with pale orange head patches, large head and small body, wearing a russet hooded archer tunic with a little wooden bow, walks with a white/pale orange warrior cat and healer cat toward the outpost. On the far right, a small gray genetically modified mouse with large ears, four legs and subtly glowing green eyes peeks from grass; no gore. Crisp carefully clustered pixel art, limited natural greens, honey ochre, cream, muted terracotta; retro game illustration, no blur, no painterly brushwork, no outlines thinner than the pixel grid. Wide cinematic meadow with the cats occupying center-right, open pale blue sky and calm dark green shaded vegetation toward left for title space. Bottom quarter calm dark grassy foreground reserved for UI overlay. Warm morning, hopeful defensive expedition. No text, no logos, no lettering, no UI, no watermark. Produce a finished production-quality landscape illustration.

## Validation

Run `dotnet build godot-client/DefenseGame.csproj`, import assets in Godot .NET, then run `--headless --path godot-client --script res://tests/loading_art_regression.gd`. Existing stage selection and combat regressions cover the skill preparation entry route. Open `loading_screen.tscn` to inspect composition without requiring a server.
