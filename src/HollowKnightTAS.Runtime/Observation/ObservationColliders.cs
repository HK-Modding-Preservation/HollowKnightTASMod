using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Observation
{
    // Pure managed geometry from read-only definitions, transforms and bounds. Never allocate
    // even a temporary Mesh/GameObject: that would consume global Unity instance IDs.
    internal sealed class ObservationColliders
    {
        private const int ArcSegments = 64;
        private sealed class PathData
        {
            internal Vector3[] Points = Array.Empty<Vector3>();
            internal bool Closed;
        }

        internal object Capture(Collider2D collider, int componentIndex, Camera? camera, string classification)
        {
            var paths = Paths(collider, out var definition, out bool approximate, out string? note, out string semantics);
            bool active = collider.enabled && collider.gameObject.activeInHierarchy;
            return ObservationData.Map("componentIndex", componentIndex, "instanceId", collider.GetInstanceID(), "type", collider.GetType().FullName,
                "enabled", collider.enabled, "activeInHierarchy", collider.gameObject.activeInHierarchy,
                "isTrigger", collider.isTrigger, "usedByComposite", collider.usedByComposite,
                "layer", collider.gameObject.layer, "layerName", LayerMask.LayerToName(collider.gameObject.layer),
                "classification", classification, "offset", ObservationData.Vector(collider.offset),
                "bounds", ObservationData.Bounds(collider.bounds), "definition", definition,
                "worldPaths", paths.Select(WorldPath).ToArray(), "screenPaths", camera == null ? Array.Empty<object>() : paths.Select(path => ScreenPath(path, camera)).ToArray(),
                "geometry", ObservationData.Map("source", "readOnlyColliderDefinitionAndBounds", "approximation", approximate,
                    "note", note, "pathSemantics", semantics, "pathCount", paths.Count, "pointCount", paths.Sum(path => path.Points.Length),
                    "arcSegments", approximate ? ArcSegments : (int?)null, "boundsAvailable", active,
                    "screenCoordinates", "normalized-full-viewport-top-left", "physicsTransformSynchronization", "notRequested",
                    "configuredShapeOnly", !active || collider.usedByComposite, "nativeObjectAllocation", false));
        }

        internal static bool Visible(Collider2D collider, Camera? camera)
        {
            if (camera == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.usedByComposite) return false;
            var body = collider.attachedRigidbody;
            if (body != null && !body.simulated) return false;
            // Invisible collision layers must still appear: do not use camera.cullingMask.
            var bounds = collider.bounds;
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            bool inFront = false;
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                {
                    var point = camera.WorldToViewportPoint(new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                        y == 0 ? bounds.min.y : bounds.max.y, bounds.center.z));
                    inFront |= point.z >= camera.nearClipPlane && point.z <= camera.farClipPlane;
                    minX = Math.Min(minX, point.x); maxX = Math.Max(maxX, point.x);
                    minY = Math.Min(minY, point.y); maxY = Math.Max(maxY, point.y);
                }
            return inFront && camera.rect.width > 0 && camera.rect.height > 0 && maxX >= 0 && minX <= 1 && maxY >= 0 && minY <= 1;
        }

        private static List<PathData> Paths(Collider2D collider, out object definition, out bool approximate, out string? note, out string semantics)
        {
            approximate = false; note = null; semantics = "outlinePaths";
            var result = new List<PathData>();
            var transform = collider.transform;
            void Add(IEnumerable<Vector2> points, bool closed)
            {
                result.Add(new PathData { Closed = closed, Points = points.Select(point => transform.TransformPoint(point + collider.offset)).ToArray() });
            }
            var scale = transform.lossyScale;
            float radiusScale = Math.Max(Math.Abs(scale.x), Math.Abs(scale.y));
            float edgeRadius = 0;
            if (collider is BoxCollider2D box)
            {
                definition = ObservationData.Map("size", ObservationData.Vector(box.size), "edgeRadius", box.edgeRadius);
                Vector2 half = box.size * 0.5f;
                Add(new[] { new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y), new Vector2(half.x, half.y), new Vector2(-half.x, half.y) }, true);
                edgeRadius = box.edgeRadius;
            }
            else if (collider is CircleCollider2D circle)
            {
                var center = transform.TransformPoint(circle.offset);
                float radius = circle.radius * radiusScale;
                bool fromBounds = collider.enabled && collider.gameObject.activeInHierarchy && collider.bounds.size.x > 0;
                if (fromBounds) { center = collider.bounds.center; radius = collider.bounds.extents.x; }
                definition = ObservationData.Map("radius", circle.radius, "worldRadius", radius, "worldRadiusSource", fromBounds ? "activeColliderBounds" : "definitionMaxAbsoluteXYScale");
                result.Add(Circle(center, radius)); approximate = true;
                note = fromBounds ? "Circle tessellated to 64 segments from live world bounds."
                    : "Inactive circle tessellated to 64 segments; nonuniform/sheared transforms use a configured approximation.";
            }
            else if (collider is CapsuleCollider2D capsule)
            {
                bool vertical = capsule.direction == CapsuleDirection2D.Vertical;
                Vector2 direction = (Vector2)transform.TransformVector(vertical ? Vector3.up : Vector3.right);
                if (direction.sqrMagnitude <= 0) direction = vertical ? Vector2.up : Vector2.right;
                direction.Normalize();
                float width = Math.Abs(capsule.size.x * scale.x), height = Math.Abs(capsule.size.y * scale.y);
                float radius = (vertical ? width : height) * 0.5f;
                float halfLine = Math.Max(0, (vertical ? height : width) * 0.5f - radius);
                var center = transform.TransformPoint(capsule.offset);
                string fit = "definitionAndLossyScale";
                float denominator = Math.Abs(direction.x) - Math.Abs(direction.y);
                if (collider.enabled && collider.gameObject.activeInHierarchy && Math.Abs(denominator) > 0.05f)
                {
                    var bounds = collider.bounds;
                    float candidateLine = (bounds.extents.x - bounds.extents.y) / denominator;
                    float candidateRadius = bounds.extents.x - candidateLine * Math.Abs(direction.x);
                    if (candidateLine >= 0 && candidateRadius > 0)
                    {
                        center = bounds.center; radius = candidateRadius; halfLine = candidateLine; fit = "axisAndLiveBounds";
                    }
                }
                definition = ObservationData.Map("size", ObservationData.Vector(capsule.size), "direction", Enum.GetName(typeof(CapsuleDirection2D), capsule.direction),
                    "worldAxis", ObservationData.Vector(direction), "worldRadius", radius, "worldHalfLine", halfLine, "worldFit", fit);
                result.Add(Capsule(center - (Vector3)(direction * halfLine), center + (Vector3)(direction * halfLine), radius));
                approximate = true;
                note = "64-segment capsule; world fit uses axis and live bounds where well-conditioned. Nonuniform/sheared transforms and diagonal fallback are explicitly approximate.";
            }
            else if (collider is PolygonCollider2D polygon)
            {
                var localPaths = new List<object>();
                for (int i = 0; i < polygon.pathCount; i++)
                {
                    var path = polygon.GetPath(i); Add(path, true); localPaths.Add(path.Select(ObservationData.Vector).ToArray());
                }
                definition = ObservationData.Map("pathCount", polygon.pathCount, "localPaths", localPaths);
            }
            else if (collider is EdgeCollider2D edge)
            {
                var points = edge.points; Add(points, false); edgeRadius = edge.edgeRadius;
                definition = ObservationData.Map("points", points.Select(ObservationData.Vector).ToArray(), "edgeRadius", edge.edgeRadius);
            }
            else if (collider is CompositeCollider2D composite)
            {
                var localPaths = new List<object>();
                for (int i = 0; i < composite.pathCount; i++)
                {
                    var points = new Vector2[composite.GetPathPointCount(i)];
                    int count = composite.GetPath(i, points);
                    bool closed = composite.geometryType == CompositeCollider2D.GeometryType.Polygons || (count > 1 && points[0] == points[count - 1]);
                    Add(points.Take(count), closed); localPaths.Add(points.Take(count).Select(ObservationData.Vector).ToArray());
                }
                definition = ObservationData.Map("geometryType", Enum.GetName(typeof(CompositeCollider2D.GeometryType), composite.geometryType),
                    "pathCount", composite.pathCount, "pointCount", composite.pointCount, "edgeRadius", composite.edgeRadius, "localPaths", localPaths);
                edgeRadius = composite.edgeRadius;
            }
            else
            {
                definition = ObservationData.Omitted("unsupportedColliderType"); approximate = true;
                note = "No shape reconstruction for this collider type; bounds and component details remain available.";
            }
            if (edgeRadius > 0)
            {
                foreach (var path in result.ToArray())
                {
                    int segments = path.Closed ? path.Points.Length : path.Points.Length - 1;
                    for (int i = 0; i < segments; i++) result.Add(Capsule(path.Points[i], path.Points[(i + 1) % path.Points.Length], edgeRadius * radiusScale));
                }
                approximate = true; semantics = "corePathsAndUnionOfSweptSegmentPrimitives";
                note = "Nonzero edgeRadius is shown as a union of 64-segment capsules; transformed radius uses maximum absolute XY scale. Raw edgeRadius and live bounds remain authoritative; this scaled reconstruction is approximate.";
            }
            return result;
        }

        private static PathData Circle(Vector3 center, float radius)
        {
            var points = new Vector3[ArcSegments];
            for (int i = 0; i < points.Length; i++)
            {
                double angle = i * Math.PI * 2 / points.Length;
                points[i] = center + new Vector3(radius * (float)Math.Cos(angle), radius * (float)Math.Sin(angle), 0);
            }
            return new PathData { Closed = true, Points = points };
        }

        private static PathData Capsule(Vector3 start, Vector3 end, float radius)
        {
            var delta = end - start;
            double heading = Math.Atan2(delta.y, delta.x);
            var points = new List<Vector3>();
            for (int i = 0; i <= ArcSegments / 2; i++)
            {
                double angle = heading - Math.PI * 0.5 + Math.PI * i / (ArcSegments / 2);
                points.Add(end + new Vector3(radius * (float)Math.Cos(angle), radius * (float)Math.Sin(angle), 0));
            }
            for (int i = 0; i <= ArcSegments / 2; i++)
            {
                double angle = heading + Math.PI * 0.5 + Math.PI * i / (ArcSegments / 2);
                points.Add(start + new Vector3(radius * (float)Math.Cos(angle), radius * (float)Math.Sin(angle), 0));
            }
            return new PathData { Closed = true, Points = points.ToArray() };
        }

        private static object WorldPath(PathData path) => ObservationData.Map("closed", path.Closed, "points", path.Points.Select(ObservationData.Vector).ToArray());
        private static object ScreenPath(PathData path, Camera camera)
        {
            var rect = camera.rect;
            return ObservationData.Map("closed", path.Closed, "points", path.Points.Select(point =>
            {
                var viewport = camera.WorldToViewportPoint(point);
                return ObservationData.Map("x", rect.x + viewport.x * rect.width, "y", 1 - (rect.y + viewport.y * rect.height), "z", viewport.z);
            }).ToArray());
        }
    }
}
