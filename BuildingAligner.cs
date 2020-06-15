using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Planetbase;
using HarmonyLib;

namespace Tahvohck_Mods.JPFariasUpdates
{
    using ModData = UnityModManagerNet.UnityModManager.ModEntry;

    public class BuildingAligner
    {
        public static bool Rendering = false;
        public static readonly string GroupName = "Connections";
        public static int LayerMask = 256;
        public static int NumRotationalSegments = 24;
        public static int NumSteps = 12;
        public static int NumSigDots = 3;   // Should evenly divide NumSteps. Will effectively round up else.
        public static float MinDistToCheck = 12f;
        public static float MaxDistToCheck = 31f;

        private static Harmony _Harmony;
        private static List<Module> LastListModules;
        private static Vector3 LastInputPoint = Vector3.zero;
        private static Vector3 LastSnapPoint = Vector3.zero;
        private static Dictionary<Module, List<PositionsLine>> ModuleLineCache =
            new Dictionary<Module, List<PositionsLine>>();

        internal static Module ActiveModule;
        internal static int ActiveModuleSize = 0;

        [LoaderOptimization(LoaderOptimization.NotSpecified)]
        public static void Init(ModData data)
        {
            data.OnUpdate = Update;
            _Harmony = new Harmony(typeof(BuildingAligner).FullName);
            try {
                _Harmony.PatchAll();
            } catch (HarmonyException e) {
                data.Logger.Error(e.Message);
            }
        }

        public static void Update(ModData data, float tDelta)
        {
            var gameState = GameManager.getInstance().getGameState() as GameStateGame;
            if (!(gameState is null) && gameState.CurrentState() != GameStateHelper.Mode.PlacingModule) {
                Rendering = false;
                DebugRenderer.ClearGroup(GroupName);
            }
        }

        /// <summary>
        /// Draw the lines and dots around a location, as well as the closest dot to that location.
        /// </summary>
        /// <param name="location"></param>
        /// <returns></returns>
        public static Vector3 RenderAvailablePositions(Vector3 location)
        {
            float closestDistance = float.MaxValue;
            float floorHeight = TerrainGenerator.getInstance().getFloorHeight();
            float hoverHeight = 0.5f;
            Vector3 closestPosition = location;
            int sigSplit = NumSteps / NumSigDots;
            bool useCachedData = Vector3.Distance(location, LastInputPoint) < 0.1f;

            List<Module> nearModules;
            // If we want to use cached data, we can skip most of this function.
            // Otherwise, update the cache.
            if (useCachedData) {
                return LastSnapPoint;
            } else {
                nearModules = ModuleHelper.GetAllModules(module =>
                {
                    float dist = Vector3.Distance(module.getPosition(), location);
                    bool inRange = dist < MaxDistToCheck * 2;
                    bool isNotActiveModule = module != ActiveModule;
                    return inRange && isNotActiveModule;
                });
                LastListModules = nearModules;
                LastInputPoint = location;
                // Only clear the group if we're not using cache
                DebugRenderer.ClearGroup(GroupName);
            }

            foreach (Module module in nearModules) {
                bool flipflop = true;
                List<PositionsLine> lines;

                // Attempt to use cached data. We don't need to use the cached flag here
                // because if cache is set, we skip this block entirely. This is only a check
                // to see if the module has data in the dict already.
                if (ModuleLineCache.ContainsKey(module)) {
                    lines = ModuleLineCache[module];
                } else {
                    lines = GetPositionsAroundModule(module);
                    ModuleLineCache.Add(module, lines);
                }

                foreach (var line in lines) {
                    var startPoint = line.First();
                    var endPoint = line.Last();
                    startPoint.y = floorHeight;
                    endPoint.y = floorHeight;
                    bool renderedAnyDot = false;

                    // line is a List<Vector3>, so iterate through it to draw the points.
                    foreach (Vector3 point in line) {
                        Vector3 pointOnFloor = point;
                        pointOnFloor.y = floorHeight;
                        // This math will put the dots at the end of each section.
                        bool isSignificantPoint = line.IndexOf(point) % sigSplit == (sigSplit - 1);
                        bool canLink = Connection.canLink(
                            ActiveModule, module,
                            pointOnFloor, module.getPosition());
                        bool canPlace = module.canPlaceModule(
                            pointOnFloor, Vector3.up, ActiveModuleSize);

                        if (canLink && canPlace) {
                            renderedAnyDot = true;
                            var tmpPoint = pointOnFloor;
                            tmpPoint.y += hoverHeight;

                            // Dots are red and large if significant, else blue and small.
                            DebugRenderer.AddCube(
                                GroupName, tmpPoint,
                                (isSignificantPoint) ? Color.blue : Color.red,
                                (isSignificantPoint) ? 0.75f : 0.25f);

                            // If the distance to this point is less than the last closest,
                            // update the closest.
                            float dist = Vector3.Distance(pointOnFloor, location);
                            if (dist < closestDistance) {
                                closestDistance = dist;
                                closestPosition = pointOnFloor;
                            }
                        }
                    }

                    // Only render the line if a dot was rendered, but always flip-flop the color.
                    if (renderedAnyDot) {
                        var tmpStart = startPoint;
                        var tmpEnd = endPoint;
                        tmpStart.y += hoverHeight;
                        tmpEnd.y += hoverHeight;

                        // Alternate colors, draw the line.
                        DebugRenderer.AddLine(
                            GroupName,
                            tmpStart, tmpEnd,
                            (flipflop) ? Color.blue : Color.green,
                            0.25f);
                    }
                    flipflop = !flipflop;
                }
            }

            Rendering = true;
            LastSnapPoint = closestPosition;
            return closestPosition;
        }

        /// <summary>
        /// Returns a list of lines, which are lists of positions, centered on a module
        /// </summary>
        public static List<PositionsLine> GetPositionsAroundModule(Module m)
        {
            List<PositionsLine> lines = new List<PositionsLine>();

            float rotationDegrees = 360f / NumRotationalSegments;
            float stepSize = (MaxDistToCheck - MinDistToCheck) / (NumSteps - 1);
            Vector3 center = m.getPosition();
            Vector3 direction = m.getTransform().forward;
            Quaternion rotation = Quaternion.Euler(0f, rotationDegrees, 0f);

            // for each rotation
            for (int rotIDX = 0; rotIDX < NumRotationalSegments; rotIDX++) {
                // for each step on that rotation
                List<Vector3> positions = new List<Vector3>();
                for (int stepIDX = 0; stepIDX < NumSteps; stepIDX++) {
                    // add a new position at that step
                    positions.Add(center + direction * (MinDistToCheck + stepSize * stepIDX));
                }
                lines.Add(new PositionsLine(positions));
                direction = rotation * direction;
            }

            return lines;
        }
    }


    [HarmonyPatch(typeof(GameStateGame), "tryPlaceModule")]
    public class PatchTryPlaceModule
    {
        public static void Postfix()
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Physics.Raycast(ray, out RaycastHit raycastHit, 150f, BuildingAligner.LayerMask);
            bool altIsDown = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            if (altIsDown) {
                var game = GameManager.getInstance().getGameState() as GameStateGame;
                BuildingAligner.ActiveModule = game.GetActiveModule();
                BuildingAligner.ActiveModuleSize = game.GetActiveModuleSizeIndex();

                var newLocation = BuildingAligner.RenderAvailablePositions(raycastHit.point);
                BuildingAligner.ActiveModule.setPosition(newLocation);
                //TryAlign(ref raycastHit);
            }

            // Only run this if we just released a key and the other alt isn't also down
            if ((Input.GetKeyUp(KeyCode.LeftAlt) || Input.GetKeyUp(KeyCode.RightAlt)) && !altIsDown) {
                BuildingAligner.Rendering = false;
                DebugRenderer.ClearGroup(BuildingAligner.GroupName);
            }
        }
    }

    public class PositionsLine : IEnumerable<Vector3>
    {
        public Vector3 StartPoint;
        public Vector3 EndPoint;
        public List<Vector3> AllPoints;

        /// <summary>
        /// Create Line from existing list of vectors.
        /// </summary>
        /// <param name="points"></param>
        public PositionsLine(List<Vector3> points)
        {
            StartPoint = points.First();
            EndPoint = points.Last();
            AllPoints = points;
        }

        /// <summary>
        /// Create Line from a starting point.
        /// </summary>
        /// <param name="startPoint"></param>
        public PositionsLine(Vector3 startPoint) : this(new List<Vector3>() { startPoint }) { }

        public void Add(Vector3 point)
        {
            EndPoint = point;
            AllPoints.Add(point);
        }

        public int IndexOf(Vector3 point)
        {
            return AllPoints.IndexOf(point);
        }

        public IEnumerator<Vector3> GetEnumerator()
        {
            return ((IEnumerable<Vector3>)AllPoints).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<Vector3>)AllPoints).GetEnumerator();
        }
    }
}
