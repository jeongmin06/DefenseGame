using DefenseGame.Client.Data;
using Godot;
using System;

namespace DefenseGame.Client.Visuals;

public static class UnitVisualConfigurator
{
    public const string IdleAnimation = "idle";
    public const string MoveAnimation = "move";
    public const string ActionAnimation = "action";
    private const string ChromaKeyShaderPath = "res://shaders/chroma_key.gdshader";

    public static void Apply(DirectionalAnimatedSprite sprite, UnitVisualDefinition visual)
    {
        if (visual.SpriteSheet is null || visual.FrameSize.X <= 0 || visual.FrameSize.Y <= 0)
            throw new ArgumentException("Unit visual requires a sprite sheet and positive frame size.", nameof(visual));

        var frames = new SpriteFrames();
        foreach (StringName animation in frames.GetAnimationNames()) frames.RemoveAnimation(animation);
        AddAnimation(frames, visual, IdleAnimation, visual.Idle);
        AddAnimation(frames, visual, MoveAnimation, visual.Move);
        AddAnimation(frames, visual, ActionAnimation, visual.Action);
        sprite.SpriteFrames = frames;
        sprite.Position = visual.SpritePosition;
        sprite.Scale = visual.SpriteScale;
        sprite.SourceFacesLeft = visual.SourceFacesLeft;
        sprite.FlipH = false;
        sprite.Material = visual.UseChromaKey
            ? new ShaderMaterial { Shader = ResourceLoader.Load<Shader>(ChromaKeyShaderPath) }
            : null;
    }

    private static void AddAnimation(
        SpriteFrames frames,
        UnitVisualDefinition visual,
        string name,
        UnitAnimationDefinition? animation)
    {
        if (animation is null) return;
        var animationName = new StringName(name);
        frames.AddAnimation(animationName);
        frames.SetAnimationLoopMode(
            animationName,
            animation.Loop ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);
        frames.SetAnimationSpeed(animationName, animation.FramesPerSecond);
        int columns = visual.SpriteSheet.GetWidth() / visual.FrameSize.X;
        for (int offset = 0; offset < animation.FrameCount; offset++)
        {
            int frameIndex = animation.StartFrame + offset;
            var texture = new AtlasTexture
            {
                Atlas = visual.SpriteSheet,
                Region = new Rect2(
                    frameIndex % columns * visual.FrameSize.X,
                    frameIndex / columns * visual.FrameSize.Y,
                    visual.FrameSize.X,
                    visual.FrameSize.Y)
            };
            frames.AddFrame(animationName, texture);
        }
    }
}
