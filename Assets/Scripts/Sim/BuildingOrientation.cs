using System.Collections.Generic;
using UnityEngine;

namespace SteelCity.Sim
{
    /// <summary>
    /// Detects building orientation by scanning terrain for adjacent roads.
    /// Determines which faces of a building are street-facing, whether the
    /// building is on a corner, and which direction the door should face.
    ///
    /// Road layout (from VoxelTerrainBuilder):
    ///   Horizontal roads at Z = -(row ± 0.5 - centerRow) * spacing
    ///   Vertical roads at   X = (col ± 0.5 - centerCol) * spacing
    ///
    /// Compass: -Z = North (red), +Z = South (blue), -X = West (yellow), +X = East (green)
    /// </summary>
    public static class BuildingOrientation
    {
        /// <summary>
        /// Which directions face a street.
        /// </summary>
        [System.Flags]
        public enum StreetFaces : byte
        {
            None  = 0,
            North = 1,   // -Z
            South = 2,   // +Z
            East  = 4,   // +X
            West  = 8,   // -X
        }

        /// <summary>
        /// Result of orientation analysis for a building.
        /// </summary>
        public struct OrientationResult
        {
            public StreetFaces streetFaces;       // which faces have roads
            public bool isCorner;                  // 2+ street faces
            public Vector3 doorDirection;          // normalized direction the door faces
            public string doorStreetName;          // name of the street the door faces
            public Vector3 cornerPosition;         // if corner, the world position of the corner
            public string cornerStreetA;           // first street at corner
            public string cornerStreetB;           // second street at corner
        }

        /// <summary>
        /// Analyze a building's orientation by checking which sides have adjacent roads.
        /// Uses the VoxelCollisionWorld to probe for road material (asphalt/cobblestone).
        /// </summary>
        /// <param name="collisionWorld">The voxel collision world with terrain data</param>
        /// <param name="buildingCenter">World-space center of the building</param>
        /// <param name="buildingSize">World-space size (width, height, depth) of the building</param>
        /// <param name="probeDistance">How far to probe for road (should reach past sidewalk)</param>
        public static OrientationResult Analyze(
            VoxelCollisionWorld collisionWorld,
            Vector3 buildingCenter,
            Vector3 buildingSize,
            float probeDistance = 2f)
        {
            var result = new OrientationResult();

            if (collisionWorld == null || !collisionWorld.IsInitialized)
                return result;

            float halfW = buildingSize.x * 0.5f;
            float halfD = buildingSize.z * 0.5f;

            // Probe points: just outside each face of the building, at ground level
            // North face = -Z, South face = +Z, East face = +X, West face = -X
            float probeY = collisionWorld.VoxelSize * 2f; // terrain surface = 2 voxels thick
            float faceOffset = 0.05f; // just outside the building face

            bool northRoad = ProbeForRoad(collisionWorld,
                buildingCenter + new Vector3(0, probeY, -halfD - faceOffset), Vector3.forward * -1f, probeDistance);
            bool southRoad = ProbeForRoad(collisionWorld,
                buildingCenter + new Vector3(0, probeY, halfD + faceOffset), Vector3.forward, probeDistance);
            bool eastRoad = ProbeForRoad(collisionWorld,
                buildingCenter + new Vector3(halfW + faceOffset, probeY, 0), Vector3.right, probeDistance);
            bool westRoad = ProbeForRoad(collisionWorld,
                buildingCenter + new Vector3(-halfW - faceOffset, probeY, 0), Vector3.right * -1f, probeDistance);

            if (northRoad) result.streetFaces |= StreetFaces.North;
            if (southRoad) result.streetFaces |= StreetFaces.South;
            if (eastRoad)  result.streetFaces |= StreetFaces.East;
            if (westRoad)  result.streetFaces |= StreetFaces.West;

            int faceCount = CountFaces(result.streetFaces);
            result.isCorner = faceCount >= 2;

            // Determine door direction — prefer the face with the widest road frontage
            // For corner buildings, pick the primary street (first found: N > S > E > W)
            if (northRoad) result.doorDirection = Vector3.forward * -1f; // -Z = north
            else if (southRoad) result.doorDirection = Vector3.forward;  // +Z = south
            else if (eastRoad) result.doorDirection = Vector3.right;     // +X = east
            else if (westRoad) result.doorDirection = Vector3.right * -1f; // -X = west
            else result.doorDirection = Vector3.forward; // fallback: south

            // Calculate corner position if on a corner
            if (result.isCorner)
            {
                // Corner = intersection of the two street-facing directions
                Vector3 cornerOffset = Vector3.zero;
                if (northRoad) cornerOffset.z = -halfD;
                else if (southRoad) cornerOffset.z = halfD;
                if (eastRoad) cornerOffset.x = halfW;
                else if (westRoad) cornerOffset.x = -halfW;

                result.cornerPosition = buildingCenter + cornerOffset;
            }

            return result;
        }

        /// <summary>
        /// Probe outward from a building face to check if there's road material
        /// (asphalt=104 or cobblestone=105) within the probe distance.
        /// Scans along the direction in voxel-sized steps.
        /// </summary>
        static bool ProbeForRoad(VoxelCollisionWorld world, Vector3 origin, Vector3 direction, float distance)
        {
            float vs = world.VoxelSize;
            Vector3 dirNorm = direction.normalized;

            // Scan from the face outward in voxel-sized steps
            int maxSteps = Mathf.CeilToInt(distance / vs);
            for (int step = 0; step <= maxSteps; step++)
            {
                Vector3 samplePos = origin + dirNorm * (step * vs);
                Vector3 local = (samplePos - world.GridOrigin) / vs;
                int vx = Mathf.FloorToInt(local.x);
                int vz = Mathf.FloorToInt(local.z);

                // Check terrain layers (y=0 and y=1 for 2-voxel-thick terrain)
                for (int vy = 0; vy < 4; vy++)
                {
                    byte mat = world.GetVoxelAtGrid(new Vector3Int(vx, vy, vz));
                    if (mat == 104 || mat == 105) // Asphalt or Cobblestone = road
                        return true;
                }
            }

            return false;
        }

        static int CountFaces(StreetFaces faces)
        {
            int count = 0;
            if ((faces & StreetFaces.North) != 0) count++;
            if ((faces & StreetFaces.South) != 0) count++;
            if ((faces & StreetFaces.East) != 0) count++;
            if ((faces & StreetFaces.West) != 0) count++;
            return count;
        }

        /// <summary>
        /// Get a human-readable street direction name.
        /// </summary>
        public static string DirectionName(Vector3 dir)
        {
            if (dir == Vector3.zero) return "None";
            if (dir.z < -0.5f) return "North";
            if (dir.z > 0.5f) return "South";
            if (dir.x > 0.5f) return "East";
            if (dir.x < -0.5f) return "West";
            return dir.ToString();
        }
    }
}
