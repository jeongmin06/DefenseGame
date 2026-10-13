using Godot;
using DefenseGame.Client.Combat;

namespace DefenseGame.Client.Visuals;

// Decorative only: the deployment grid remains the source of terrain and input rules.
public partial class BattlefieldArt : Node2D
{
    private DeploymentGrid _grid = null!;

    public override void _Ready()
    {
        ZIndex = 1; // Above terrain fills, below units and projectiles.
        Callable.From(Initialize).CallDeferred();
    }

    private void Initialize()
    {
        _grid = GetParent().GetNode<DeploymentGrid>("DeploymentGrid");
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_grid is null) return;
        for (int y = 0; y < _grid.Rows; y++)
        for (int x = 0; x < _grid.Columns; x++)
        {
            var cell = new Vector2I(x, y);
            Vector2 p = _grid.Position + new Vector2(x, y) * _grid.CellSize;
            var type = _grid.GetTileType(cell);
            // Keep all decoration inset so hover, occupancy and placement outlines stay clear.
            for (int i = 0; i < 5; i++)
            {
                int seed = x * 73 + y * 131 + i * 37;
                float inset = 13;
                float span = Mathf.Max(1, _grid.CellSize - inset * 2 - 8);
                Vector2 q = p + new Vector2(inset + seed % (int)span, inset + (seed * 7 + i * 11) % (int)span);
                if (type == DeploymentGrid.TileType.Ground)
                {
                    Pixel(q, 6, 2, "71944b");
                    Pixel(q + new Vector2(2, -4), 2, 6, "75994e");
                    Pixel(q + new Vector2(6, -2), 2, 4, "547d3a");
                }
                else if (type == DeploymentGrid.TileType.EnemyPath)
                {
                    Pixel(q, 8, 2, "c49a66");
                    Pixel(q + new Vector2(2, 3), 4, 2, "ad8554");
                }
            }
            if (type == DeploymentGrid.TileType.Blocked)
            {
                Vector2 q = p + new Vector2(_grid.CellSize * .25f, _grid.CellSize * .35f);
                Pixel(q + new Vector2(-3, 20), 39, 8, "53614a");
                Pixel(q + new Vector2(0, 6), 32, 18, "7d886b");
                Pixel(q + new Vector2(6, 0), 23, 18, "99a182");
                Pixel(q + new Vector2(6, 2), 18, 4, "b0b595");
                Pixel(q + new Vector2(24, 12), 8, 12, "69765c");
                Pixel(q + new Vector2(-5, 22), 14, 4, "82934f");
            }
        }
        // The narrow outer verge is outside the playable grid.
        for (int y = 122; y < 588; y += 36)
        {
            Verge(new Vector2(4, y));
            Verge(new Vector2(1250, y + 12));
        }
    }

    private void Verge(Vector2 p)
    {
        Pixel(p + new Vector2(0, 8), 26, 18, "4e7539");
        Pixel(p + new Vector2(4, 2), 18, 20, "688b43");
        Pixel(p + new Vector2(8, 0), 12, 6, "85a151");
        Pixel(p + new Vector2(14, 10), 4, 4, "c4bb6c");
    }

    private void Pixel(Vector2 position, float width, float height, string color) =>
        DrawRect(new Rect2(position.Round(), new Vector2(width, height)), new Color(color));
}
