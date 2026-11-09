using SGO.Server.Infrastructure.WorldData;

// Uses the exact same Blocking.dat validation and A* helper as the server.
// A random coordinate is never accepted solely because it lies in the radius.
internal sealed class BotWalkPlanner
{
    private readonly BlockingMapService _blocking;
    private readonly MonsterNavigationService _navigation;
    private readonly int _map;
    private readonly int _radius;
    private readonly (int X, int Y) _center;

    public string GameDataRoot { get; }

    public BotWalkPlanner(int map, int centerX, int centerY, int radius)
    {
        _map = map;
        _radius = radius;
        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "GameData"),
            Path.Combine(Directory.GetCurrentDirectory(), "server", "bin",
                "Release", "net8.0", "GameData"),
            Path.Combine(Directory.GetCurrentDirectory(), "server", "bin",
                "Debug", "net8.0", "GameData"),
            Path.Combine(Directory.GetCurrentDirectory(), "Old", "服務端", "Dat")
        };
        GameDataRoot = roots.FirstOrDefault(root =>
            new BlockingMapService(root).HasMap(map))
            ?? throw new InvalidOperationException(
                "Cannot find Blocking.dat for the chosen map. Build the server first.");
        _blocking = new BlockingMapService(GameDataRoot);

        _navigation = new MonsterNavigationService(_blocking);
        // Round user-supplied pixel coordinates to walkable cell centers.
        var anchor = FindNear(centerX, centerY, 96)
            ?? throw new ArgumentException(
                "No walkable tile near the requested observation center.");
        _center = anchor;
    }

    public (int X, int Y) Center => _center;

    private bool Walkable(int x, int y)
        => _blocking.GetFlags(_map, x, y) == 0;

    private (int X, int Y)? FindNear(int x, int y, int radius)
    {
        var size = BlockingMapService.CellSize;
        var cx = (int)Math.Floor(x / (double)size);
        var cy = (int)Math.Floor(y / (double)size);
        for (var r = 0; r <= radius / size; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            for (var dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    continue;
                var px = (cx + dx) * size + size / 2;
                var py = (cy + dy) * size + size / 2;
                if (Walkable(px, py))
                    return (px, py);
            }
        }
        return null;
    }

    public (int X, int Y) RandomPoint()
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            var angle = Random.Shared.NextDouble() * Math.PI * 2;
            var dist = Math.Sqrt(Random.Shared.NextDouble()) * _radius;
            var rawX = _center.X + Math.Cos(angle) * dist;
            var rawY = _center.Y + Math.Sin(angle) * dist;
            var size = BlockingMapService.CellSize;
            var px = (int)Math.Floor(rawX / size) * size + size / 2;
            var py = (int)Math.Floor(rawY / size) * size + size / 2;

            if (!Walkable(px, py))
                continue;
            // The observer and the chosen actor must be on the same reachable
            // component, even if they are on opposite sides of a small wall.
            if (_blocking.ValidateMove(_map,
                _center.X, _center.Y, px, py, false).Allowed)
                return (px, py);
        }
        // Explicit walkable fallback, rather than teleporting into a wall.
        return _center;
    }

    public bool TryStep(int fromX, int fromY, double goalX, double goalY,
        out int nextX, out int nextY)
    {
        nextX = fromX;
        nextY = fromY;
        if (!_navigation.TryNextStep(_map, fromX, fromY,
            goalX, goalY, 16, out var nx, out var ny))
            return false;

        var ix = (int)Math.Round(nx);
        var iy = (int)Math.Round(ny);
        if (ix == fromX && iy == fromY)
            return false;
        if (!_blocking.ValidateMove(_map, fromX, fromY,
            ix, iy, false).Allowed)
            return false;
        nextX = ix;
        nextY = iy;
        return true;
    }
}
