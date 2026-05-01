using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.Reflection;
using StardewValley.Buildings;
using StardewValley.Objects;
using StardewValley.GameData.Buildings;

namespace UltimateCoopAndBarn
{
    public interface IContentPatcherAPI
    {
        bool IsConditionsApiReady { get; }
        void RegisterToken(IManifest mod, string name, Func<IEnumerable<string>> getValue);
    }
    public class ModEntry : Mod
    {
        public static ModEntry? modInstance;
        public static IContentPack? cpPack;
        internal const string UltimateCP = "bobkalonger.UltimateCoopAndBarnCP_";
        internal const string SVExpandCP = "FlashShifter.StardewValleyExpandedCP_";
        internal const string UltimateBarn = $"{UltimateCP}UltimateBarn";
        internal const string UltimateCoop = $"{UltimateCP}UltimateCoop";
        internal const string SuperDenseBarn = $"{UltimateCP}SuperDenseBarn";
        internal const string SuperDenseCoop = $"{UltimateCP}SuperDenseCoop";
        internal const string UltimatePremiumCoop = $"{SVExpandCP}PremiumCoop";
        internal const string UltimatePremiumBarn = $"{SVExpandCP}PremiumBarn";
        private const string VppItemKey = "Ultimate/vppItems";
        public override void Entry(IModHelper helper)
        {
            modInstance = this;

            I18n.Init(Helper.Translation);

            var mi = Helper.ModRegistry.Get("bobkalonger.UltimateCoopAndBarnCP");
            if (mi != null)
                cpPack = mi.GetType().GetProperty("ContentPack")?.GetValue(mi) as IContentPack;

            Helper.Events.GameLoop.ReturnedToTitle += (s, e) =>
            {
                _cachedUpgradeConfig = null;
                _lastMode = null;
            };
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.Player.Warped += PlayerOnWarped;

            var harmony = new Harmony(this.ModManifest.UniqueID);

            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        ///<inheritdoc cref="IGameLoopEvents.GameLaunched"/>
        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            var cp = Helper.ModRegistry.GetApi<IContentPatcherAPI>("Pathoschild.ContentPatcher");
            if (cp is null)
            {
                Monitor.Log("Content Patcher not found; dynamic tokens will not be available.", LogLevel.Warn);
                return;
            }

            cp.RegisterToken(ModManifest, "UltimateMode", GetUltimateMode);       
        }

        private IEnumerable<string> GetUltimateMode()
        {
            if (!Context.IsWorldReady)
            {
                return Array.Empty<string>();
            }
            return new[] { ComputeUltimateMode() };
        }
        private string? _lastMode;
        private string? _cachedUpgradeConfig = null;
        private string GetUpgradeConfig()
        {
            if (_cachedUpgradeConfig != null) return _cachedUpgradeConfig;
            var config = cpPack?.ReadJsonFile<Dictionary<string, string>>("config.json");
            if (config != null && config.TryGetValue("Ultimate Building Upgrade", out string? value))
                _cachedUpgradeConfig = value;
            return _cachedUpgradeConfig ?? "Auto";
        }
        private string ComputeUltimateMode()
        {
            string upgradeChoice = GetUpgradeConfig();

            bool hasJMCB = Helper.ModRegistry.IsLoaded("jenf1.megacoopbarn");
            bool hasUARC = Helper.ModRegistry.IsLoaded("UncleArya.ResourceChickens");

            string result;

            if (upgradeChoice != "Auto")
            {
                string manual = upgradeChoice;

                bool validSelection = manual switch
                {
                    "SVE"  => Helper.ModRegistry.IsLoaded("FlashShifter.StardewValleyExpandedCP"),
                    "Giga" => Helper.ModRegistry.IsLoaded("bobkalonger.gigacoopnbarn"),
                    "Mega" => Helper.ModRegistry.IsLoaded("jenf1.megacoopbarn") || Helper.ModRegistry.IsLoaded("UncleArya.ResourceChickens"),
                    _      => false
                };

                if (validSelection)
                {
                    if (manual == "Mega")
                    {
                        if (hasJMCB && hasUARC) return "Both";
                        if (hasJMCB) return "Mega";
                        return "Giant";
                    }
                    else result = manual;
                }
                else
                {
                    Monitor.Log($"Config set to '{manual}' but that mod isn't installed, falling back on automatic behavior.", LogLevel.Warn);
                    result = ComputeAuto(hasJMCB, hasUARC);
                }
            }
            else
            {
                result = ComputeAuto(hasJMCB, hasUARC);
            }

            if (result != _lastMode)
            {
                _lastMode = result;
                Helper.GameContent.InvalidateCache("Data/Buildings");
            }

            return result;
        }

        private string ComputeAuto(bool hasJMCB, bool hasUARC)
        {
            if (Helper.ModRegistry.IsLoaded("FlashShifter.StardewValleyExpandedCP"))
            {
                if (Helper.ModRegistry.IsLoaded("JennaJuffuffles.BiggerOnTheInside.CP")) return "BOTI";
                return "SVE";
            }
            ;
            if (Helper.ModRegistry.IsLoaded("bobkalonger.gigacoopnbarn")) return "Giga";
            if (hasJMCB && hasUARC) return "Both";
            if (hasJMCB) return "Mega";
            if (hasUARC) return "Giant";
            return "Vanilla";
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

        private static void ShiftObjectsInRect(GameLocation interior, Rectangle sourceRect, int xShift, HashSet<string>? excludedIds = null)
        {
            var toMove = interior.objects.Pairs
                .Where(p => sourceRect.Contains((int)p.Key.X, (int)p.Key.Y))
                .Where(p => excludedIds == null || !excludedIds.Contains(p.Value.QualifiedItemId))
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

        private static void BarnItemMovesToVPP(GameLocation interior)
        {
            if (interior.map == null) return;
            var groundFloor = new Rectangle(2, 19, 59, 27);
            var loft = new Rectangle(22, 6, 19, 7);
            ShiftObjectsInRect(interior, groundFloor, 5);
            ShiftObjectsInRect(interior, loft, 5);
        }

        private static void BarnItemMovesToBase(GameLocation interior)
        {
            if (interior.map == null) return;

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
            ShiftObjectsInRect(interior, groundFloor, -5);
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

            var groundFloor = new Rectangle(2, 6, 34, 38);
            var entranceNook = new Rectangle(36, 36, 6, 4);
            var excluded = new HashSet<string> { "(BC)101" };

            ShiftObjectsInRect(interior, groundFloor, 5, excluded);
            ShiftObjectsInRect(interior, entranceNook, 10);
        }

        private static void CoopItemMovestoBase(GameLocation interior)
        {
            if (interior.map == null) return;

            var landingPad = new Rectangle(20, 7, 16, 36);
            var excluded = new HashSet<string> { "(BC)101" };

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
                if (interior.objects.ContainsKey(shiftedIncubatorPositions[i]))
                    MoveObjectTo(interior, shiftedIncubatorPositions[i], IncubatorBasePositions[i]);
            }
        }

        private void PlayerOnWarped(object? sender, WarpedEventArgs e)
        {
            RemoveCustomlights(e.OldLocation);

            if (e.NewLocation is AnimalHouse)
            {
                foreach (var building in Game1.getFarm().buildings)
                {
                    if (building.GetIndoors() == e.NewLocation &&
                        building.buildingType.Value is UltimateBarn or UltimateCoop or SuperDenseBarn or SuperDenseCoop)
                    {
                        Helper.GameContent.InvalidateCache(e.NewLocation.mapPath.Value);
                        e.NewLocation.reloadMap();
                        building.updateInteriorWarps(e.NewLocation);

                        bool vppActive = IsVppOvercrowdingActive();
                        building.modData.TryGetValue(VppItemKey, out string lastVppState);
                        string targetVppState = vppActive ? "VPP" : "Base";

                        if (lastVppState != targetVppState)
                        {
                            if (building.buildingType.Value is UltimateBarn or SuperDenseBarn)
                            {
                                if (vppActive) BarnItemMovesToVPP(e.NewLocation);
                                else BarnItemMovesToBase(e.NewLocation);
                            }
                            else if (building.buildingType.Value is UltimateCoop or SuperDenseCoop)
                            {
                                if (vppActive) CoopItemMovestoVPP(e.NewLocation);
                                else CoopItemMovestoBase(e.NewLocation);
                            }
                            building.modData[VppItemKey] = targetVppState;
                        }
                        break;
                    }
                }
            }

            foreach (var b in e.NewLocation.buildings)
            {
                if (b.buildingType.Value == UltimateBarn)
                {
                    var ultimateLightBL = new Point(b.tileX.Value + 3, b.tileY.Value + 3);
                    var ll = new LightSource($"{UltimateCP}BarnLight_{b.tileX.Value}_{b.tileY.Value}_L", 4, ultimateLightBL.ToVector2() * Game1.tileSize, 1f, Color.Black, LightSource.LightContext.None);
                    Game1.currentLightSources.Add(ll.Id, ll);

                    var ultimateLightBR = new Point(b.tileX.Value + 8, b.tileY.Value + 3);
                    var lr = new LightSource($"{UltimateCP}BarnLight_{b.tileX.Value}_{b.tileY.Value}_R", 4, ultimateLightBR.ToVector2() * Game1.tileSize, 1f, Color.Black, LightSource.LightContext.None);
                    Game1.currentLightSources.Add(lr.Id, lr);
                }

                if (b.buildingType.Value == UltimateCoop)
                {
                    var ultimateLightC = new Point(b.tileX.Value + 6, b.tileY.Value + 2);
                    var lc = new LightSource($"{UltimateCP}CoopLight_{b.tileX.Value}_{b.tileY.Value}", 4, ultimateLightC.ToVector2() * Game1.tileSize, 1f, Color.Black, LightSource.LightContext.None);
                    Game1.currentLightSources.Add(lc.Id, lc);
                }

                if (b.buildingType.Value == UltimatePremiumCoop)
                {
                    var ultimateLightPCP = new Point(b.tileX.Value + 6, b.tileY.Value + 2);
                    var lp = new LightSource($"{UltimateCP}PremiumCoopLight_patch_{b.tileX.Value}_{b.tileY.Value}", 4, ultimateLightPCP.ToVector2() * Game1.tileSize, 1f, Color.Black, LightSource.LightContext.None);
                    Game1.currentLightSources.Add(lp.Id, lp);
                }                
            }
        }

        private static void RemoveCustomlights(GameLocation location)
        {
            if (location == null || !Context.IsWorldReady)
                return;

            var toRemove = Game1.currentLightSources.Keys
                .Where(k => k.StartsWith(UltimateCP))
                .ToList();
            foreach (var key in toRemove)
                Game1.currentLightSources.Remove(key);
        }

        private void MakeIncubatorsMoveable(AnimalHouse indoors)
        {
            foreach (var pair in indoors.Objects.Pairs)
            {
                StardewValley.Object obj = pair.Value;

                if (obj.QualifiedItemId == "(BC)101" && obj.questItem.Value)
                {
                    obj.questItem.Value = false;
                }
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            foreach (Building building in Game1.getFarm().buildings)
            {
                if (building.indoors.Value is AnimalHouse indoors)
                {
                    MakeIncubatorsMoveable(indoors);
                }
            }
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            foreach (Building building in Game1.getFarm().buildings)
            {
                if (building.indoors.Value is AnimalHouse indoors)
                {
                    MakeIncubatorsMoveable(indoors);
                }
            }

            bool vppActive = IsVppOvercrowdingActive();
            Monitor.Log($"VPP active: {vppActive}", LogLevel.Debug);
            
            foreach (Building building in Game1.getFarm().buildings)
            {
                if (building.buildingType.Value is not (UltimateBarn or UltimateCoop or SuperDenseBarn or SuperDenseCoop))
                    continue;

                var interior = building.GetIndoors();

                if (building.daysUntilUpgrade.Value > 0 || interior == null) continue;

                string upgradeKey = $"{ModManifest.UniqueID}/buildingKey";
                string currentLevel = building.buildingType.Value;
                building.modData.TryGetValue(upgradeKey, out string lastMovedLevel);

                Monitor.Log($"Building: {building.buildingType.Value}, upgradeKey match: {lastMovedLevel == currentLevel}", LogLevel.Debug);
                
                if (lastMovedLevel != currentLevel)
                {
                    if (building.buildingType.Value is UltimateBarn or SuperDenseBarn)
                        BarnItemMoves(interior);
                    else if (building.buildingType.Value is UltimateCoop or SuperDenseCoop)
                        CoopItemMoves(interior);

                    if (vppActive)
                    {
                        if (building.buildingType.Value is UltimateBarn or SuperDenseBarn)
                            BarnItemMovesToVPP(interior);
                        else if (building.buildingType.Value is UltimateCoop or SuperDenseCoop)
                            CoopItemMovestoVPP(interior);
                    }

                    building.modData[upgradeKey] = currentLevel;
                    building.modData[VppItemKey] = vppActive ? "VPP" : "Base";
                    continue;
                }
                
                building.modData.TryGetValue(VppItemKey, out string lastVppState);
                string targetVppState = vppActive ? "VPP" : "Base";

                Monitor.Log($"VppItemKey check — lastVppState: '{lastVppState}', targetVppState: '{targetVppState}', match: {lastVppState == targetVppState}", LogLevel.Debug);

                if (lastVppState == targetVppState) continue;

                if (building.buildingType.Value is (UltimateBarn or SuperDenseBarn))
                {
                    if (vppActive) BarnItemMovesToVPP(interior);
                    else BarnItemMovesToBase(interior);
                }
                else if (building.buildingType.Value is (UltimateCoop or SuperDenseCoop))
                {
                    if (vppActive) CoopItemMovestoVPP(interior);
                    else CoopItemMovestoBase(interior);
                }
                building.modData[VppItemKey] = targetVppState;
                Monitor.Log($"Triggering VPP item moves for {building.buildingType.Value}, vppActive: {vppActive}", LogLevel.Debug);
            }
        }

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

        private static void BarnItemMoves(GameLocation interior)
        {
            if (interior.map == null) return;
            
            var namedDestinations = new Dictionary<string, Vector2>
            {
                { "(BC)99",  new Vector2(26, 19) },
                { "(BC)104", new Vector2(41, 19) },
                { "(BC)165", new Vector2(36, 19) },
                { "(BC)272", new Vector2(21, 19) }
            };

            var spawnIfMissing = new HashSet<string> { "(BC)104", "(BC)165", "(BC)272" };

            var haySlots = new List<Rectangle>
            {
                new Rectangle(4,  29, 12, 1),
                new Rectangle(4,  39, 12, 1),
                new Rectangle(47, 29, 12, 1),
                new Rectangle(47, 39, 12, 1)
            };

            Vector2 startCenter = new Vector2(
                interior.map.Layers[0].LayerWidth / 2,
                interior.map.Layers[0].LayerHeight / 2
            );

            var foundHay = SpiralSearch(interior, "(O)178", startCenter, maxRadius: 50);
            int haySlotIndex = 0;
            int haySlotX = haySlots[0].Left;

            foreach (var (sourceTile, obj) in foundHay)
            {
                if (haySlotIndex >= haySlots.Count) break;

                Vector2 dest = new Vector2(haySlotX, haySlots[haySlotIndex].Top);
                interior.removeObject(sourceTile, false);
                obj.TileLocation = dest;
                interior.objects[dest] = obj;

                haySlotX++;
                if (haySlotX >= haySlots[haySlotIndex].Right)
                {
                    haySlotIndex++;
                    if (haySlotIndex < haySlots.Count)
                        haySlotX = haySlots[haySlotIndex].Left;
                }
            }

            var excludedIds = new HashSet<string>(namedDestinations.Keys) { "(O)178" };

            foreach (var kvp in namedDestinations)
            {
                var found = SpiralSearch(interior, kvp.Key, startCenter, maxRadius: 50);

                if (found.Count == 0)
                {
                    if (spawnIfMissing.Contains(kvp.Key))
                    {
                        if (interior.objects.TryGetValue(kvp.Value, out StardewValley.Object blocking))
                        {
                            interior.objects.Remove(kvp.Value);
                            Game1.player.team.returnedDonations.Add(blocking);
                            Game1.player.team.newLostAndFoundItems.Value = true;
                        }

                        var newObj = ItemRegistry.Create(kvp.Key) as StardewValley.Object;
                        if (newObj != null)
                        {
                            newObj.TileLocation = kvp.Value;
                            interior.objects[kvp.Value] = newObj;
                        }
                    }
                    continue;
                }

                var (sourceTile, obj) = found[0];
                interior.removeObject(sourceTile, false);
                obj.TileLocation = kvp.Value;
                interior.objects[kvp.Value] = obj;
            }

            var landingPad = new Rectangle(x: 21, y: 21, width: 21, height: 24);

            var barnItemMoves = interior.objects.Pairs
                .Where(p => !excludedIds.Contains(p.Value.QualifiedItemId))
                .ToList();
                
            
            foreach (var pair in barnItemMoves)
            {
                Vector2 dest = LandingPadRect(interior, landingPad);
                if (dest == Vector2.Zero) continue;
                interior.removeObject(pair.Key, false);
                pair.Value.TileLocation = dest;
                interior.objects[dest] = pair.Value;
            }

            Vector2 correctFeederTile = namedDestinations["(BC)99"];
            var extraFeeders = SpiralSearch(interior, "(BC)99", startCenter, maxRadius: 50)
                .Where(f => f.tile != correctFeederTile)
                .ToList();
            foreach (var (tile, _) in extraFeeders)
                interior.removeObject(tile, false);
        }
            
        private static void CoopItemMoves(GameLocation interior)
        {
            if (interior.map == null) return;

            var namedDestinations = new Dictionary<string, Vector2>
            {
                { "(BC)99",  new Vector2(38, 36) },
                { "(BC)104", new Vector2(28, 6)  },
                { "(BC)165", new Vector2(39, 36) },
                { "(BC)272", new Vector2(29, 6)  }
            };

            var spawnIfMissing = new HashSet<string> { "(BC)104", "(BC)165", "(BC)272" };

            var haySlots = new List<Rectangle>
            {
                new Rectangle(4,  14, 12, 1),
                new Rectangle(4,  22, 12, 1),
                new Rectangle(4,  30, 12, 1),
                new Rectangle(4,  38, 12, 1)
            };

            Vector2 startCenter = new Vector2(
                interior.map.Layers[0].LayerWidth / 2,
                interior.map.Layers[0].LayerHeight / 2
            );

            var foundHay = SpiralSearch(interior, "(O)178", startCenter, maxRadius: 50);
            int haySlotIndex = 0;
            int haySlotX = haySlots[0].Left;

            foreach (var (sourceTile, obj) in foundHay)
            {
                if (haySlotIndex >= haySlots.Count) break;

                Vector2 dest = new Vector2(haySlotX, haySlots[haySlotIndex].Top);
                interior.removeObject(sourceTile, false);
                obj.TileLocation = dest;
                interior.objects[dest] = obj;

                haySlotX++;
                if (haySlotX >= haySlots[haySlotIndex].Right)
                {
                    haySlotIndex++;
                    if (haySlotIndex < haySlots.Count)
                        haySlotX = haySlots[haySlotIndex].Left;
                }

            }

            Vector2[] incubatorDestinations =
            {
                new Vector2(2, 14),
                new Vector2(2, 22),
                new Vector2(2, 30),
                new Vector2(2, 38)
            };

            var foundIncubators = SpiralSearch(interior, "(BC)101", startCenter, maxRadius: 50);

            for (int i = 0; i < foundIncubators.Count && i < incubatorDestinations.Length; i++)
            {
                var (sourceTile, obj) = foundIncubators[i];
                Vector2 dest = incubatorDestinations[i];
                interior.removeObject(sourceTile, false);
                obj.TileLocation = dest;
                interior.objects[dest] = obj;
            }

            for (int i = foundIncubators.Count; i < incubatorDestinations.Length; i++)
            {
                Vector2 dest = incubatorDestinations[i];

                if (interior.objects.TryGetValue(dest, out StardewValley.Object blocking))
                {
                    interior.objects.Remove(dest);
                    Game1.player.team.returnedDonations.Add(blocking);
                    Game1.player.team.newLostAndFoundItems.Value = true;

                }

                var newIncubator = ItemRegistry.Create("(BC)101") as StardewValley.Object;
                if (newIncubator != null)
                {
                    newIncubator.TileLocation = dest;
                    interior.objects[dest] = newIncubator;
                }
            }

            var excludedIds = new HashSet<string>(namedDestinations.Keys) { "(O)178", "(BC)101" };

            foreach (var kvp in namedDestinations)
            {
                var found = SpiralSearch(interior, kvp.Key, startCenter, maxRadius: 50);

                if (found.Count == 0)
                {
                    if (spawnIfMissing.Contains(kvp.Key))
                    {
                        if (interior.objects.TryGetValue(kvp.Value, out StardewValley.Object blocking))
                        {
                            interior.objects.Remove(kvp.Value);
                            Game1.player.team.returnedDonations.Add(blocking);
                            Game1.player.team.newLostAndFoundItems.Value = true;
                        }

                        var newObj = ItemRegistry.Create(kvp.Key) as StardewValley.Object;
                        if (newObj != null)
                        {
                            newObj.TileLocation = kvp.Value;
                            interior.objects[kvp.Value] = newObj;
                        }
                    }
                    continue;
                }

                var (sourceTile, obj) = found[0];
                interior.removeObject(sourceTile, false);
                obj.TileLocation = kvp.Value;
                interior.objects[kvp.Value] = obj;
            }

            var landingPad = new Rectangle(x: 20, y: 7, width: 16, height: 36);

            var coopItemMoves = interior.objects.Pairs
                .Where(p => !excludedIds.Contains(p.Value.QualifiedItemId))
                .ToList();

            foreach (var pair in coopItemMoves)
            {
                        
                Vector2 dest = LandingPadRect(interior, landingPad);
                if (dest == Vector2.Zero) continue;
                interior.removeObject(pair.Key, false);
                pair.Value.TileLocation = dest;
                interior.objects[dest] = pair.Value;
            }

            Vector2 correctFeederTile = namedDestinations["(BC)99"];
            var extraFeeders = SpiralSearch(interior, "(BC)99", startCenter, maxRadius: 50)
                .Where(f => f.tile != correctFeederTile)
                .ToList();
            foreach (var (tile, _) in extraFeeders)
                interior.removeObject(tile, false); 
        }

        [HarmonyPatch(typeof(NPC), "updateConstructionAnimation")]
        public static class RobinUpgradeBonk
        {
            public static void Postfix(NPC __instance)
            {
                if (!Game1.IsMasterGame) return;
                if (__instance.Name != "Robin") return;

                Building? building = null;

                foreach (var location in Game1.locations)
                {
                    building = location.buildings.FirstOrDefault(b =>
                            b.daysUntilUpgrade.Value > 0 &&
                            (b.upgradeName.Value == UltimateBarn || b.upgradeName.Value == UltimateCoop ||
                             b.upgradeName.Value == SuperDenseBarn || b.upgradeName.Value == SuperDenseCoop));
                    if (building != null) break;
                }

                if (building == null) return;

                GameLocation indoors = building.GetIndoors();
                if (building.daysUntilUpgrade.Value <= 0 || indoors == null) return;

                __instance.currentLocation?.characters.Remove(__instance);
                __instance.currentLocation = indoors;
                if (!indoors.characters.Contains(__instance))
                    indoors.addCharacter(__instance);

                switch (building.buildingType.Value)
                {
                    case UltimatePremiumBarn:
                        __instance.setTilePosition(2, 6);
                        break;
                    case UltimatePremiumCoop:
                        __instance.setTilePosition(27, 8);
                        __instance.flip = true;
                        break;
                    default:
                        __instance.setTilePosition(1, 5);
                        break;
                }

                __instance.ignoreScheduleToday = true;
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.FinishConstruction))]
        public static class InstantBuildingConstructionPatch
        {
            public static void Postfix(Building __instance)
            {
                if (__instance.buildingType.Value is not (UltimateBarn or UltimateCoop or SuperDenseBarn or SuperDenseCoop or UltimatePremiumBarn or UltimatePremiumCoop))
                    return;
                

                string upgradeKey = $"{modInstance!.ModManifest.UniqueID}/buildingKey";
                string currentLevel = __instance.buildingType.Value;

                __instance.modData.TryGetValue(upgradeKey, out string lastMovedLevel);
                if (lastMovedLevel == currentLevel)
                    return;

                modInstance!.Helper.Events.GameLoop.UpdateTicked += DoItemMoves;

                void DoItemMoves(object? sender, UpdateTickedEventArgs e)
                {
                    if (!e.IsMultipleOf(3)) return;
                    modInstance!.Helper.Events.GameLoop.UpdateTicked -= DoItemMoves;

                    GameLocation interior = __instance.GetIndoors();
                    if (interior == null || interior.map == null)
                        return;

                    if (__instance.buildingType.Value is UltimateBarn or SuperDenseBarn)
                        BarnItemMoves(interior);
                    else if (__instance.buildingType.Value is UltimateCoop or SuperDenseCoop)
                        CoopItemMoves(interior);

                    bool vppActive = modInstance.IsVppOvercrowdingActive();
                    if (vppActive)
                    {
                        if (__instance.buildingType.Value is UltimateBarn or SuperDenseBarn)
                            BarnItemMovesToVPP(interior);
                        else if (__instance.buildingType.Value is UltimateCoop or SuperDenseCoop)
                            CoopItemMovestoVPP(interior);
                    }
                    
                    if (interior is AnimalHouse animalHouse)
                        modInstance!.MakeIncubatorsMoveable(animalHouse);

                    __instance.modData[upgradeKey] = currentLevel;
                    __instance.modData[VppItemKey] = vppActive ? "VPP" : "Base";
                }
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.GetData))]
        public static class UltimateSignPatch
        {
            public static void Postfix(Building __instance, ref BuildingData __result)
            {
                if (__result == null)
                    return;
                
                if (__instance.upgradeName.Value is not (UltimateBarn or UltimateCoop or SuperDenseBarn or SuperDenseCoop))
                    return;

                switch (__instance.upgradeName.Value)
                {
                    case UltimateBarn:
                        __result.UpgradeSignTile = new Vector2(3.5f, 4f);
                        __result.UpgradeSignHeight = 60f;
                        break;
                    case UltimateCoop:
                        __result.UpgradeSignTile = new Vector2(4.5f, 4f);
                        __result.UpgradeSignHeight = 28f;
                        break;
                    case SuperDenseBarn:
                        __result.UpgradeSignTile = new Vector2(4.5f, 4f);
                        __result.UpgradeSignHeight = 50f;
                        break;
                    case SuperDenseCoop:
                        __result.UpgradeSignTile = new Vector2(4.5f, 4f);
                        __result.UpgradeSignHeight = 52f;
                        break;
                }
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.doesTileHaveProperty))]
        public static class UltimateCursorPatch
        {
            public static void Postfix(Building __instance, int tile_x, int tile_y, string property_name, ref string property_value, ref bool __result)
            {
                if (__instance.buildingType.Value == UltimateBarn && __instance.daysUntilUpgrade.Value <= 0)
                {
                    var interior = __instance.GetIndoors();
                    if (tile_x == __instance.tileX.Value + __instance.humanDoor.X + 8 &&
                        tile_y == __instance.tileY.Value + __instance.humanDoor.Y &&
                        interior != null)
                    {
                        if (property_name == "Action")
                        {
                            property_value = "meow";
                            __result = true;
                        }
                    }
                }
                if (__instance.buildingType.Value == UltimateCoop && __instance.daysUntilUpgrade.Value <= 0)
                {
                    var interior = __instance.GetIndoors();
                    if (tile_x == __instance.tileX.Value + __instance.humanDoor.X - 2 &&
                        tile_y == __instance.tileY.Value + __instance.humanDoor.Y - 2 &&
                        interior != null)
                    {
                        if (property_name == "Action")
                        {
                            property_value = "meow";
                            __result = true;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.doAction))]
        public static class UltimateDoorPatch
        {
            public static void Postfix(Building __instance, Vector2 tileLocation, Farmer who, ref bool __result)
            {
                if (who.ActiveObject != null && who.ActiveObject.IsFloorPathItem() && who.currentLocation != null && !who.currentLocation.terrainFeatures.ContainsKey(tileLocation))
                {
                    return;
                }

                if (__instance.buildingType.Value == UltimateBarn && __instance.daysUntilUpgrade.Value <= 0)
                {
                    var interior = __instance.GetIndoors();
                    if (tileLocation.X == __instance.tileX.Value + __instance.humanDoor.X + 8 &&
                        tileLocation.Y == __instance.tileY.Value + __instance.humanDoor.Y &&
                        interior != null)
                    {
                        if (who.mount != null)
                        {
                            Game1.showRedMessage(Game1.content.LoadString("Strings\\Buildings:DismountBeforeEntering"));
                            __result = false;
                            return;
                        }
                        if (who.team.demolishLock.IsLocked())
                        {
                            Game1.showRedMessage(Game1.content.LoadString("Strings\\Buildings:CantEnter"));
                            __result = false;
                            return;
                        }
                        if (__instance.OnUseHumanDoor(who))
                        {
                            who.currentLocation?.playSound("doorClose", tileLocation);
                            bool isStructure = __instance.indoors.Value != null;
                            if (interior.warps.Count > 1)
                                Game1.warpFarmer(interior.NameOrUniqueName, interior.warps[1].X, interior.warps[1].Y - 1, Game1.player.FacingDirection, isStructure);
                        }
                        __result = true;
                        return;
                    }
                }
                if (__instance.buildingType.Value == UltimateCoop && __instance.daysUntilUpgrade.Value <= 0)
                {
                    var interior = __instance.GetIndoors();
                    if (tileLocation.X == __instance.tileX.Value + __instance.humanDoor.X - 2 &&
                        tileLocation.Y == __instance.tileY.Value + __instance.humanDoor.Y - 2 &&
                        interior != null)
                    {
                        if (who.mount != null)
                        {
                            Game1.showRedMessage(Game1.content.LoadString("Strings\\Buildings:DismountBeforeEntering"));
                            __result = false;
                            return;
                        }
                        if (who.team.demolishLock.IsLocked())
                        {
                            Game1.showRedMessage(Game1.content.LoadString("Strings\\Buildings:CantEnter"));
                            __result = false;
                            return;
                        }
                        if (__instance.OnUseHumanDoor(who))
                        {
                            who.currentLocation?.playSound("doorClose", tileLocation);
                            bool isStructure = __instance.indoors.Value != null;
                            if (interior.warps.Count > 1)
                                Game1.warpFarmer(interior.NameOrUniqueName, interior.warps[1].X - 1, interior.warps[1].Y, Game1.player.FacingDirection, isStructure);
                        }
                        __result = true;
                        return;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.updateInteriorWarps))]
        public static class UltimateBarnWarpPatch
        {
            public static void Postfix(Building __instance, GameLocation interior)
            {
                if (__instance.buildingType.Value != UltimateBarn)
                    return;
                if (interior == null || interior.warps.Count < 2)
                    return;

                var w = interior.warps[1];
                interior.warps[1] = new(w.X, w.Y, w.TargetName, w.TargetX + 8, w.TargetY, w.flipFarmer.Value, w.npcOnly.Value);
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.updateInteriorWarps))]
        public static class UltimateCoopWarpPatch
        {
            public static void Postfix(Building __instance, GameLocation interior)
            {
                if (__instance.buildingType.Value != UltimateCoop)
                    return;
                if (interior == null || interior.warps.Count <2)
                    return;

                var w = interior.warps[1];
                interior.warps[1] = new(w.X, w.Y, w.TargetName, w.TargetX - 1, w.TargetY - 3, w.flipFarmer.Value, w.npcOnly.Value);
            }
        }

        [HarmonyPatch(typeof(Utility), "_HasBuildingOrUpgrade")]
        public static class UtilityHasCoopBarnPatch
        {
            public static void Postfix(GameLocation location, string buildingId, ref bool __result)
            {
                string? toCheck = null;
                if (buildingId == "Coop" || buildingId == "Big Coop" || buildingId == "Deluxe Coop" || buildingId == UltimatePremiumCoop)
                {
                    toCheck = UltimateCoop;
                }
                else if (buildingId == "Barn" || buildingId == "Big Barn" || buildingId == "Deluxe Barn" || buildingId == UltimatePremiumBarn)
                {
                    toCheck = UltimateBarn;
                }

                if (!__result && toCheck != null)
                {
                    if (location.getNumberBuildingsConstructed(toCheck) > 0)
                    {
                        __result = true;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.InitializeIndoor))]
        public static class BuildingAutoGrabberFix
        {
            public static void Postfix(Building __instance, bool forUpgrade)
            {
                if (!forUpgrade)
                    return;
                if (__instance.buildingType.Value != UltimateCoop &&
                    __instance.buildingType.Value != UltimateBarn)
                    return;

                foreach (var obj in __instance.indoors.Value.objects.Values)
                {
                    if (obj.QualifiedItemId == "(BC)165" && obj.heldObject.Value == null)
                    {
                        obj.heldObject.Value = new Chest();
                    }
                }
            }
        }
    }
}
