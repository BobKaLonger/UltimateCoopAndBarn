using Microsoft.Xna.Framework;
using StardewValley;

namespace UltimateCoopAndBarn
{
    public partial class ModEntry
    {
        private static List<(Vector2 tile, StardewValley.Object obj)> SpiralSearch(GameLocation location, string qualifiedId, Vector2 center, int maxRadius)
        {
            var results = new List<(Vector2, StardewValley.Object)>();

            for (int radius = 0; radius <= maxRadius; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                            continue;

                        Vector2 tile = new Vector2(center.X + dx, center.Y + dy);
                        if (location.objects.TryGetValue(tile, out StardewValley.Object obj) && obj.QualifiedItemId == qualifiedId)
                        {
                            results.Add((tile, obj));
                        }
                    }
                }
            }

            return results;
        }

        private static Vector2 LandingPadRect(GameLocation location, Rectangle landingPad)
        {
            for (int y = landingPad.Top; y < landingPad.Bottom; y++)
            {
                for (int x = landingPad.Left; x < landingPad.Right; x++)
                {
                    Vector2 candidate = new Vector2(x, y);
                    if (!location.IsTileBlockedBy(candidate, CollisionMask.Objects | CollisionMask.Furniture))
                    {
                        return candidate;
                    }
                }
            }
            return Vector2.Zero;
        }

        private static void ShiftObjectsInRect(GameLocation interior, Rectangle sourceRect, int xShift, HashSet<string>? excludedIds = null)
        {
            var toMove = interior.objects.Pairs
                .Where(p => sourceRect.Contains((int)p.Key.X, (int)p.Key.Y))
                .Where(p => excludedIds == null || !excludedIds.Contains(p.Value.QualifiedItemId))
                .OrderBy(p => xShift > 0 ? -p.Key.X : p.Key.X)
                .ToList();

            foreach (var (tile, obj) in toMove)
            {
                Vector2 dest = new Vector2(tile.X + xShift, tile.Y);
                if (interior.objects.ContainsKey(dest))
                {
                    Game1.player.team.returnedDonations.Add(interior.objects[dest]);
                    Game1.player.team.newLostAndFoundItems.Value = true;
                    interior.objects.Remove(dest);
                }
                interior.removeObject(tile, false);
                obj.TileLocation = dest;
                interior.objects[dest] = obj;
            }
        }

        private static void MoveObjectTo(GameLocation interior, Vector2 source, Vector2 dest)
        {
            if (!interior.objects.TryGetValue(source, out var obj)) return;
            if (interior.objects.ContainsKey(dest))
            {
                Game1.player.team.returnedDonations.Add(interior.objects[dest]);
                Game1.player.team.newLostAndFoundItems.Value = true;
                interior.objects.Remove(dest);
            }
            interior.removeObject(source, false);
            obj.TileLocation = dest;
            interior.objects[dest] = obj;
        }
    }
}