using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace UltimateCoopAndBarn
{
    public partial class ModEntry
    {
        private const string VppItemKey = "Ultimate/vppItems";
        private const string OvercrowdingKey = "bobkalonger.UltCB_code/OvercrowdingActive";
        private bool _overcrowdingActive = false;

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            if (e.NewMenu == null && e.OldMenu != null)
                UpdateOvercrowdingState();
        }

        private void UpdateOvercrowdingState()
        {
            bool current = IsVppOvercrowdingActive();
            if (current == _overcrowdingActive) return;
            _overcrowdingActive = current;
            Game1.player.modData[OvercrowdingKey] = current ? "true" : "false";
            Helper.GameContent.InvalidateCache("Data/Buildings");

            Utility.ForEachBuilding(building =>
            {
                if (building.buildingType.Value is UltimateBarn or UltimateCoop or SuperDenseBarn or SuperDenseCoop)
                {
                    var interior = building.GetIndoors();
                    if (interior != null)
                        building.updateInteriorWarps(interior);
                }
                return true;
            });
        }

        private bool IsVppOvercrowdingActive()
        {
            if (!Context.IsWorldReady) return false;
            if (!Helper.ModRegistry.IsLoaded("KediDili.VanillaPlusProfessions")) return false;
            if (!Helper.ModRegistry.IsLoaded("Esca.EMP")) return false;
            return GameStateQuery.CheckConditions(
                "KediDili.VanillaPlusProfessions_PlayerHasTalent Any Overcrowding",
                Game1.getFarm(),
                Game1.player
            );
        }

        private static void ReturnHayToSilo(GameLocation interior, Rectangle zone)
        {
            var farm = Game1.getFarm();
            var hayInZone = interior.objects.Pairs
                .Where(p => zone.Contains((int)p.Key.X, (int)p.Key.Y) && p.Value.QualifiedItemId == "(O)178")
                .ToList();

            foreach (var (tile, obj) in hayInZone)
            {
                interior.removeObject(tile, false);
                int leftover = farm.tryToAddHay(obj.Stack);
                if (leftover > 0)
                {
                    var leftoverHay = ItemRegistry.Create("(O)178", leftover) as StardewValley.Object;
                    if (leftoverHay != null)
                    {
                        Game1.player.team.returnedDonations.Add(leftoverHay);
                        Game1.player.team.newLostAndFoundItems.Value = true;
                    }
                }
            }
        }

        private static void BarnItemMovesToVPP(GameLocation interior)
        {
            if (interior.map == null) return;

            MoveObjectTo(interior, new Vector2(4, 29), new Vector2(16, 29));
            MoveObjectTo(interior, new Vector2(4, 39), new Vector2(16, 39));
            ShiftObjectsInRect(interior, new Rectangle(47, 29, 5, 1), 12, null);
            ShiftObjectsInRect(interior, new Rectangle(47, 39, 5, 1), 12, null);

            var groundFloor = new Rectangle(2, 19, 59, 27);
            var loft = new Rectangle(22, 6, 19, 7);
            var hayExcluded = new HashSet<string> { "(O)178" };
            ShiftObjectsInRect(interior, groundFloor, 5, hayExcluded);
            ShiftObjectsInRect(interior, loft, 5);
        }

        private static void BarnItemMovesToBase(GameLocation interior)
        {
            if (interior.map == null) return;

            ReturnHayToSilo(interior, new Rectangle(17, 29, 4, 1));
            ReturnHayToSilo(interior, new Rectangle(17, 39, 4, 1));
            ReturnHayToSilo(interior, new Rectangle(64, 29, 4, 1));
            ReturnHayToSilo(interior, new Rectangle(64, 39, 4, 1));

            MoveObjectTo(interior, new Vector2(16, 29), new Vector2(4, 29));
            MoveObjectTo(interior, new Vector2(16, 39), new Vector2(4, 39));
            ShiftObjectsInRect(interior, new Rectangle(59, 29, 5, 1), -12, null);
            ShiftObjectsInRect(interior, new Rectangle(59, 39, 5, 1), -12, null);

            var landingPad = new Rectangle(21, 21, 21, 24);
            var edgeZones = new[]
            {
                new Rectangle(2, 19, 5, 27),
                new Rectangle(66, 19, 5, 27),
                new Rectangle(22, 6, 5, 7),
                new Rectangle(46, 6, 5, 7)
            };

            foreach (var zone in edgeZones)
            {
                var edgeItems = interior.objects.Pairs
                    .Where(p => zone.Contains((int)p.Key.X, (int)p.Key.Y))
                    .Where(p => p.Value.QualifiedItemId != "(O)178")
                    .ToList();

                foreach (var (tile, obj) in edgeItems)
                {
                    Vector2 dest = LandingPadRect(interior, landingPad);
                    interior.removeObject(tile, false);
                    if (dest != Vector2.Zero)
                    {
                        obj.TileLocation = dest;
                        interior.objects[dest] = obj;
                    }
                    else
                    {
                        Game1.player.team.returnedDonations.Add(obj);
                        Game1.player.team.newLostAndFoundItems.Value = true;
                    }
                }
            }

            var groundFloor = new Rectangle(7, 19, 59, 27);
            var loft = new Rectangle(27, 6, 19, 7);
            var hayExcluded = new HashSet<string> { "(O)178" };
            ShiftObjectsInRect(interior, groundFloor, -5, hayExcluded);
            ShiftObjectsInRect(interior, loft, -5);
        }

        private static readonly Vector2[] IncubatorBasePositions =
        {
            new Vector2(2, 14),
            new Vector2(2, 22),
            new Vector2(2, 30),
            new Vector2(2, 38)
        };

        private static void CoopItemMovestoVPP(GameLocation interior)
        {
            if (interior.map == null) return;

            MoveObjectTo(interior, new Vector2(4, 14), new Vector2(16, 14));
            MoveObjectTo(interior, new Vector2(4, 22), new Vector2(16, 22));
            MoveObjectTo(interior, new Vector2(4, 30), new Vector2(16, 30));
            MoveObjectTo(interior, new Vector2(4, 38), new Vector2(16, 38));

            var groundFloor = new Rectangle(2, 6, 34, 38);
            var entranceNook = new Rectangle(36, 36, 6, 4);
            var excluded = new HashSet<string> { "(BC)101", "(O)178" };

            ShiftObjectsInRect(interior, groundFloor, 5, excluded);
            ShiftObjectsInRect(interior, entranceNook, 10);
        }

        private static void CoopItemMovestoBase(GameLocation interior)
        {
            if (interior.map == null) return;

            ReturnHayToSilo(interior, new Rectangle(17, 14, 4, 1));
            ReturnHayToSilo(interior, new Rectangle(17, 22, 4, 1));
            ReturnHayToSilo(interior, new Rectangle(17, 30, 4, 1));
            ReturnHayToSilo(interior, new Rectangle(17, 38, 4, 1));

            MoveObjectTo(interior, new Vector2(16, 14), new Vector2(4, 14));
            MoveObjectTo(interior, new Vector2(16, 22), new Vector2(4, 22));
            MoveObjectTo(interior, new Vector2(16, 30), new Vector2(4, 30));
            MoveObjectTo(interior, new Vector2(16, 38), new Vector2(4, 38));

            var landingPad = new Rectangle(20, 7, 16, 36);
            var excluded = new HashSet<string> { "(BC)101", "(O)178" };

            var edgeZones = new[]
            {
                new Rectangle(2, 6, 5, 38),
                new Rectangle(41, 6, 5, 38)
            };

            foreach (var zone in edgeZones)
            {
                var edgeItems = interior.objects.Pairs
                    .Where(p => zone.Contains((int)p.Key.X, (int)p.Key.Y))
                    .Where(p => !excluded.Contains(p.Value.QualifiedItemId))
                    .ToList();

                foreach (var (tile, obj) in edgeItems)
                {
                    Vector2 dest = LandingPadRect(interior, landingPad);
                    interior.removeObject(tile, false);
                    if (dest != Vector2.Zero)
                    {
                        obj.TileLocation = dest;
                        interior.objects[dest] = obj;
                    }
                    else
                    {
                        Game1.player.team.returnedDonations.Add(obj);
                        Game1.player.team.newLostAndFoundItems.Value = true;
                    }
                }
            }

            var groundFloor = new Rectangle(7, 6, 34, 38);
            var entranceNook = new Rectangle(46, 36, 6, 4);

            ShiftObjectsInRect(interior, groundFloor, -5, excluded);
            ShiftObjectsInRect(interior, entranceNook, -10);

            var shiftedIncubatorPositions = new[]
            {
                new Vector2(7, 14),
                new Vector2(7, 22),
                new Vector2(7, 30),
                new Vector2(7, 38)
            };

            for (int i = 0; i < shiftedIncubatorPositions.Length; i++)
            {
                if (interior.objects.TryGetValue(shiftedIncubatorPositions[i], out var obj) && obj.QualifiedItemId == "(BC)101")
                    MoveObjectTo(interior, shiftedIncubatorPositions[i], IncubatorBasePositions[i]);
            }
        }
    }
}