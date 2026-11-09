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
        // Requested map positions may be visual landmarks or blocked cells.
        // Search within the spawn radius before failing. The original 96px
        // search rejected Map13 (4331,5346), whose nearest clear tile is
        // (4424,5528), approximately 204px away.
        var searchRadius = Math.Max(radius, 256);
        var anchor = FindNear(centerX, centerY, searchRadius)
            ?? throw new ArgumentException(
                $"No walkable tile within {searchRadius}px of ({centerX},{centerY}) " +
                $"on Map{map}. Choose a different center or increase --radius.");
        _center = anchor;
        var offset = Math.Sqrt(
            Math.Pow(_center.X - centerX, 2) +
            Math.Pow(_center.Y - centerY, 2));
        if (offset > BlockingMapService.CellSize)
        {
            Console.WriteLine(
                $"[BOTS] Center ({centerX},{centerY}) is not walkable; " +
                $"using nearest valid ({_center.X},{_center.Y}) " +
                $"distance={offset:F0}px (Blocking.dat).");
        }
    }

    public (int X, int Y) Center => _center;

    private bool Walkable(int x, int y)
        => _blocking.GetFlags(_map, x, y) == 0;

    private (int X, int Y)? FindNear(int x, int y, int radius)
    {
        var size = BlockingMapService.CellSize;
        var cx = (int)Math.Floor(x / (double)size);
        var cy = (int)Math.Floor(y / (double)size);
        var cells = (int)Math.Ceiling(radius / (double)size) + 1;
        var maxDistanceSquared = (long)radius * radius;
        var bestDistanceSquared = long.MaxValue;
        (int X, int Y)? best = null;

        for (var dy = -cells; dy <= cells; dy++)
        for (var dx = -cells; dx <= cells; dx++)
        {
            var px = (cx + dx) * size + size / 2;
            var py = (cy + dy) * size + size / 2;
            var squared = (long)(px - x) * (px - x) +
                          (long)(py - y) * (py - y);
            if (squared > maxDistanceSquared ||
                squared >= bestDistanceSquared ||
                !Walkable(px, py))
                continue;

            best = (px, py);
            bestDistanceSquared = squared;
        }
        return best;
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
