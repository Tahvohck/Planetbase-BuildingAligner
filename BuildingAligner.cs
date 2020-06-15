using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Planetbase;

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

        internal static Module ActiveModule;
        internal static int ActiveModuleSize = 0;

        [LoaderOptimization(LoaderOptimization.NotSpecified)]
        public static void Init(ModData data)
        {
            data.OnUpdate = Update;
        }

        public static void Update(ModData data, float tDelta)
        {
            var gameState = GameManager.getInstance().getGameState() as GameStateGame;
            if (!(gameState is null) && gameState.CurrentState() != GameStateHelper.Mode.PlacingModule) {
                Rendering = false;
                DebugRenderer.ClearGroup(GroupName);
            } else if (!(gameState is null) &&
                gameState.CurrentState() == GameStateHelper.Mode.PlacingModule) {
                PatchTryPlace.Postfix();
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
            Vector3 closestPosition = location;
            int sigSplit = NumSteps / NumSigDots;


            List<Module> nearModules = ModuleHelper.GetAllModules(module => {
                float dist = Vector3.Distance(module.getPosition(), location);
                bool inRange = dist < MaxDistToCheck * 2;
                bool isNotActiveModule = module != ActiveModule;
                return inRange && isNotActiveModule;
            });

            foreach (Module module in nearModules) {
                var lines = GetPositionsAroundModule(module);
                bool flipflop = true;

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
                            tmpPoint.y += 2f;

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
                        tmpStart.y += 2f;
                        tmpEnd.y += 2f;

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
            return closestPosition;
        }

        /// <summary>
        /// Returns a list of lines, which are lists of positions, centered on a module
        /// </summary>
        public static List<List<Vector3>> GetPositionsAroundModule(Module m)
        {
            List<List<Vector3>> lines = new List<List<Vector3>>();

            float rotationDegrees = 360f / NumRotationalSegments;
            float stepSize = (MaxDistToCheck - MinDistToCheck) / (NumSteps - 1);
            Vector3 center = m.getPosition();
            Vector3 direction = m.getTransform().forward;
            Quaternion rotation = Quaternion.Euler(0f, rotationDegrees, 0f);

            // for each rotation
            for (int rotIDX = 0; rotIDX < NumRotationalSegments; rotIDX++) {
                // for each step on that rotation
                List<Vector3> positions = new List<Vector3>();
                lines.Add(positions);
                for (int stepIDX = 0; stepIDX < NumSteps; stepIDX++) {
                    // add a new position at that step
                    positions.Add(center + direction * (MinDistToCheck + stepSize * stepIDX));
                }
                direction = rotation * direction;
            }

            return lines;
        }
    }


    public class PatchTryPlace
    {
        public static void Postfix()
        {
            DebugRenderer.ClearGroup("Connections");
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Physics.Raycast(ray, out RaycastHit raycastHit, 150f, BuildingAligner.LayerMask);

            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) {
                var game = GameManager.getInstance().getGameState() as GameStateGame;
                BuildingAligner.ActiveModule = game.GetActiveModule();
                BuildingAligner.ActiveModuleSize = game.GetActiveModuleSizeIndex();

                raycastHit.point = BuildingAligner.RenderAvailablePositions(raycastHit.point);
                //TryAlign(ref raycastHit);
            } else {
                BuildingAligner.Rendering = false;
                DebugRenderer.ClearGroup(BuildingAligner.GroupName);
            }
        }
    }
}
