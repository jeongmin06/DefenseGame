using Godot;

namespace DefenseGame.Client.UI;

public enum OperationUiTone
{
    Neutral,
    Primary,
    Success,
    Danger
}

public static class OperationUiStyle
{
    public const string InteractionBoundMeta = "operation_ui_interaction_bound";

    public static readonly Color Text = new("e8eef0");
    public static readonly Color MutedText = new("9aabb2");
    public static readonly Color Amber = new("e8a94f");
    public static readonly Color Teal = new("55d6c2");
    public static readonly Color Red = new("ef6f68");

    private static readonly Color Panel = new("14232dcc");
    private static readonly Color PanelStrong = new("101c25ee");
    private static readonly Color Neutral = new("243642ee");
    private static readonly Color NeutralHover = new("304957f2");
    private static readonly Color Disabled = new("18252cbb");

    public static StyleBoxFlat PanelStyle(bool strong = false, Color? border = null, int radius = 8)
    {
        return Box(strong ? PanelStrong : Panel, border ?? new Color("49616ccc"), 1, radius, 14);
    }

    public static void ApplyButton(
        Button button,
        OperationUiTone tone = OperationUiTone.Neutral,
        bool selected = false,
        bool repairRequired = false,
        int fontSize = 15,
        float leftPadding = 14)
    {
        Color accent = repairRequired ? Red : selected ? Amber : ToneColor(tone);
        Color normal = repairRequired
            ? new Color("4a282fee")
            : selected ? new Color("4c3a28f2") : Neutral;
        Color hover = repairRequired
            ? new Color("613039f5")
            : selected ? new Color("62492cf5") : NeutralHover;

        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.AddThemeColorOverride("font_color", repairRequired ? new Color("ffd6d2") : Text);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_focus_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", new Color("718087"));
        button.AddThemeStyleboxOverride("normal", Box(normal, accent, selected || repairRequired ? 2 : 1, 6, leftPadding));
        button.AddThemeStyleboxOverride("hover", Box(hover, accent.Lightened(0.12f), 2, 6, leftPadding));
        button.AddThemeStyleboxOverride("pressed", Box(hover.Darkened(0.08f), accent, 2, 6, leftPadding));
        button.AddThemeStyleboxOverride("focus", Box(new Color("314a58f5"), Amber, 2, 6, leftPadding));
        Color disabledBorder = repairRequired || tone == OperationUiTone.Danger
            ? Red.Darkened(0.35f)
            : tone == OperationUiTone.Success ? Teal.Darkened(0.35f) : new Color("3b4a50aa");
        Color disabledBackground = repairRequired || tone == OperationUiTone.Danger
            ? new Color("352329cc")
            : tone == OperationUiTone.Success ? new Color("183836cc") : Disabled;
        button.AddThemeStyleboxOverride("disabled", Box(disabledBackground, disabledBorder, 1, 6, leftPadding));
        BindInteraction(button);
    }

    public static void ApplyPanel(Panel panel, bool strong = false, Color? border = null)
    {
        panel.AddThemeStyleboxOverride("panel", PanelStyle(strong, border));
    }

    public static Color StatusColor(bool isError, bool isBusy = false) => isError ? Red : isBusy ? Amber : Teal;

    private static Color ToneColor(OperationUiTone tone) => tone switch
    {
        OperationUiTone.Primary => Amber,
        OperationUiTone.Success => Teal,
        OperationUiTone.Danger => Red,
        _ => new Color("607985")
    };

    private static void BindInteraction(Button button)
    {
        button.FocusMode = Control.FocusModeEnum.All;
        button.MouseDefaultCursorShape = button.Disabled
            ? Control.CursorShape.Arrow
            : Control.CursorShape.PointingHand;
        if (button.HasMeta(InteractionBoundMeta)) return;

        button.SetMeta(InteractionBoundMeta, true);
        button.Resized += () => button.PivotOffset = button.Size * 0.5f;
        button.MouseEntered += () =>
        {
            button.MouseDefaultCursorShape = button.Disabled
                ? Control.CursorShape.Arrow
                : Control.CursorShape.PointingHand;
            if (!button.Disabled) SetScale(button, 1.01f);
        };
        button.MouseExited += () => SetScale(button, button.HasFocus() && !button.Disabled ? 1.01f : 1.0f);
        button.FocusEntered += () =>
        {
            if (!button.Disabled) SetScale(button, 1.01f);
        };
        button.FocusExited += () => SetScale(button, button.IsHovered() && !button.Disabled ? 1.01f : 1.0f);
        button.ButtonDown += () =>
        {
            if (!button.Disabled) SetScale(button, 0.985f);
        };
        button.ButtonUp += () => SetScale(button, button.IsHovered() || button.HasFocus() ? 1.01f : 1.0f);
        button.TreeExiting += () => button.Scale = Vector2.One;
    }

    private static void SetScale(Button button, float scale)
    {
        if (!GodotObject.IsInstanceValid(button)) return;
        button.PivotOffset = button.Size * 0.5f;
        button.Scale = new Vector2(scale, scale);
    }

    private static StyleBoxFlat Box(Color background, Color border, int borderWidth, int radius, float leftPadding)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ContentMarginLeft = leftPadding,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        };
    }
}
