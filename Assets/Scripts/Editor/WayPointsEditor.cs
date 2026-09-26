#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Scene-view authoring tool for WayPoints routes: draw the path, drag waypoints,
// insert/remove points, and get lane/direction validation while you build.
[CustomEditor(typeof(WayPoints))]
public class WayPointsEditor : Editor
{
    private const float MinSegmentLength = 0.05f;
    private const float RecommendedSpacing = 0.5f;
    private const float SharpTurnAngle = 100f;
    private const float ReversalAngle = 165f;
    private const float LaneOverlapDistance = 1f;
    private const float CrossingDistance = 1.5f;

    private static readonly Color VehicleColor = new Color(0.25f, 0.7f, 1f);
    private static readonly Color PedestrianColor = new Color(1f, 0.8f, 0.2f);
    private static readonly Color InsertColor = new Color(0.35f, 1f, 0.4f, 0.95f);
    private static readonly Color RemoveColor = new Color(1f, 0.35f, 0.35f, 0.95f);
    private static readonly Color WarningColor = new Color(1f, 0.6f, 0.1f);
    private static readonly Color ErrorColor = new Color(1f, 0.25f, 0.25f);
    private static readonly Color UnlimitedColor = new Color(0.55f, 0.6f, 0.65f);
    private static readonly Color LowLimitColor = new Color(0.95f, 0.25f, 0.2f);
    private static readonly Color MidLimitColor = new Color(0.95f, 0.8f, 0.2f);
    private static readonly Color HighLimitColor = new Color(0.3f, 0.9f, 0.35f);

    private static bool s_ColorBySpeedLimit = true;

    private struct ValidationMessage
    {
        public bool isError;
        public string text;
        public Vector3 position;
        public bool hasPosition;

        public ValidationMessage(bool isError, string text, Vector3 position)
        {
            this.isError = isError;
            this.text = text;
            this.position = position;
            this.hasPosition = true;
        }
    }

    private readonly List<ValidationMessage> messages = new List<ValidationMessage>();
    private readonly List<ValidationMessage> sceneMessages = new List<ValidationMessage>();

    private bool showTools = true;
    private bool showValidation = true;
    private bool showGizmo = true;

    [MenuItem("GameObject/Car Town/Waypoint Route", false, 10)]
    private static void CreateRoute(MenuCommand command)
    {
        GameObject root = new GameObject("Waypoints");
        GameObjectUtility.SetParentAndAlign(root, command.context as GameObject);
        Undo.RegisterCreatedObjectUndo(root, "Create Waypoint Route");
        root.AddComponent<WayPoints>();

        for (int i = 0; i < 2; i++)
        {
            GameObject point = new GameObject("Waypoint " + i);
            point.transform.SetParent(root.transform, false);
            point.transform.localPosition = Vector3.forward * (i * 5f);
            Undo.RegisterCreatedObjectUndo(point, "Create Waypoint Route");
        }

        Selection.activeGameObject = root;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        WayPoints wayPoints = (WayPoints)target;
        Transform root = wayPoints.transform;

        EditorGUILayout.Space();
        showTools = EditorGUILayout.Foldout(showTools, "Route Tools");
        if (showTools)
        {
            DrawTools(wayPoints, root);
        }

        EditorGUILayout.Space();
        showGizmo = EditorGUILayout.Foldout(showGizmo, "Route Gizmo");
        if (showGizmo)
        {
            DrawGizmoOptions(wayPoints);
        }

        EditorGUILayout.Space();
        showValidation = EditorGUILayout.Foldout(showValidation, "Route Validation");
        if (showValidation)
        {
            ValidateInto(wayPoints, messages);
            DrawValidation();
        }
    }

    private void DrawTools(WayPoints wayPoints, Transform root)
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Waypoint"))
        {
            AddWaypoint(root);
        }

        using (new EditorGUI.DisabledScope(!HasSelectableChild(root)))
        if (GUILayout.Button("Insert After Selected"))
        {
            InsertAfterSelection(root);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Remove Last"))
        {
            RemoveWaypoint(root, root.childCount - 1);
        }

        if (GUILayout.Button("Reverse Route"))
        {
            ReverseRoute(root);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Rename Waypoints"))
        {
            RenameWaypoints(root);
        }

        if (GUILayout.Button("Flatten Y"))
        {
            FlattenY(root);
        }

        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Fit Speed Limit Array"))
        {
            FitSpeedLimits(root);
        }

        EditorGUILayout.HelpBox(
            string.Format("Kind: {0}   Waypoints: {1}   Length: {2:0.##} m", wayPoints.kind, root.childCount, RouteLength(root)),
            MessageType.None);
    }

    private void DrawValidation()
    {
        if (messages.Count == 0)
        {
            EditorGUILayout.HelpBox("No issues found.", MessageType.Info);
            return;
        }

        int errors = 0;
        int warnings = 0;
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i].isError)
            {
                errors++;
            }
            else
            {
                warnings++;
            }
        }

        EditorGUILayout.HelpBox(
            string.Format("{0} error(s), {1} warning(s).", errors, warnings),
            errors > 0 ? MessageType.Error : MessageType.Warning);

        for (int i = 0; i < messages.Count; i++)
        {
            EditorGUILayout.HelpBox(messages[i].text, messages[i].isError ? MessageType.Error : MessageType.Warning);
        }
    }

    private void OnSceneGUI()
    {
        foreach (Object selected in targets)
        {
            WayPoints wayPoints = selected as WayPoints;
            if (wayPoints == null)
            {
                continue;
            }

            DrawRoute(wayPoints);
        }
    }

    private void DrawRoute(WayPoints wayPoints)
    {
        Transform root = wayPoints.transform;
        int count = root.childCount;
        if (count == 0)
        {
            return;
        }

        Color routeColor = wayPoints.kind == WayPoints.RouteKind.Pedestrian ? PedestrianColor : VehicleColor;

        Vector3[] positions = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = root.GetChild(i).position;
        }

        float minLimit;
        float maxLimit;
        bool hasRange = GetSpeedLimitRange(wayPoints, out minLimit, out maxLimit);

        if (count >= 2)
        {
            for (int i = 0; i < count - 1; i++)
            {
                Handles.color = SegmentColor(wayPoints.GetSpeedLimit(i + 1), hasRange, minLimit, maxLimit, wayPoints.kind);
                Handles.DrawAAPolyLine(4f, positions[i], positions[i + 1]);
            }
        }

        bool editable = !Application.isPlaying;

        for (int i = 0; i < count - 1; i++)
        {
            Vector3 a = positions[i];
            Vector3 b = positions[i + 1];
            Vector3 mid = (a + b) * 0.5f;
            Vector3 direction = b - a;
            if (direction.sqrMagnitude < 1e-6f)
            {
                continue;
            }

            float limit = wayPoints.GetSpeedLimit(i + 1);
            Handles.color = SegmentColor(limit, hasRange, minLimit, maxLimit, wayPoints.kind);
            Handles.ArrowHandleCap(0, mid, Quaternion.LookRotation(direction.normalized), HandleUtility.GetHandleSize(mid) * 1.2f, EventType.Repaint);

            if (limit > 0f)
            {
                Handles.Label(mid + Vector3.up * 0.4f, string.Format("{0:0} limit", limit));
            }

            if (editable)
            {
                Handles.color = InsertColor;
                float size = HandleUtility.GetHandleSize(mid) * 0.07f;
                if (Handles.Button(mid, Quaternion.identity, size, size, Handles.SphereHandleCap))
                {
                    InsertAt(root, i + 1, mid);
                }
            }
        }

        for (int i = 0; i < count; i++)
        {
            Transform child = root.GetChild(i);
            Vector3 position = child.position;

            Handles.color = routeColor;
            Handles.SphereHandleCap(0, position, Quaternion.identity, HandleUtility.GetHandleSize(position) * 0.08f, EventType.Repaint);
            Handles.Label(position + Vector3.up * 0.6f, i + "  " + child.name);

            if (!editable)
            {
                continue;
            }

            EditorGUI.BeginChangeCheck();
            Vector3 newPosition = Handles.PositionHandle(position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(child, "Move Waypoint");
                child.position = newPosition;
                MarkDirty(child.gameObject);
            }

            Vector3 removePosition = position + Vector3.up * (HandleUtility.GetHandleSize(position) * 0.35f);
            Handles.color = RemoveColor;
            float removeSize = HandleUtility.GetHandleSize(removePosition) * 0.07f;
            if (Handles.Button(removePosition, Quaternion.identity, removeSize, removeSize, Handles.SphereHandleCap))
            {
                RemoveWaypoint(root, i);
            }
        }

        ValidateInto(wayPoints, sceneMessages);
        for (int i = 0; i < sceneMessages.Count; i++)
        {
            ValidationMessage message = sceneMessages[i];
            if (!message.hasPosition)
            {
                continue;
            }

            Handles.color = message.isError ? ErrorColor : WarningColor;
            Handles.SphereHandleCap(0, message.position + Vector3.up, Quaternion.identity, HandleUtility.GetHandleSize(message.position) * 0.12f, EventType.Repaint);
        }
    }

    private void DrawGizmoOptions(WayPoints wayPoints)
    {
        EditorGUI.BeginChangeCheck();
        s_ColorBySpeedLimit = EditorGUILayout.ToggleLeft("Color segments by speed limit", s_ColorBySpeedLimit);
        if (EditorGUI.EndChangeCheck())
        {
            SceneView.RepaintAll();
        }

        Rect swatch = EditorGUILayout.GetControlRect(false, 18f);
        float third = swatch.width / 3f;
        EditorGUI.DrawRect(new Rect(swatch.x, swatch.y, third, swatch.height), LowLimitColor);
        EditorGUI.DrawRect(new Rect(swatch.x + third, swatch.y, third, swatch.height), MidLimitColor);
        EditorGUI.DrawRect(new Rect(swatch.x + third * 2f, swatch.y, third, swatch.height), HighLimitColor);

        float minLimit;
        float maxLimit;
        bool hasRange = GetSpeedLimitRange(wayPoints, out minLimit, out maxLimit);

        EditorGUILayout.LabelField(hasRange
            ? string.Format("Slowest {0:0.#} \u2192 fastest {1:0.#}   (grey = no limit)", minLimit, maxLimit)
            : "No segment limits set: every segment is drawn grey.");
    }

    // Always-on Scene view gizmo so routes can be tuned without selecting them or entering play mode.
    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Active)]
    private static void DrawRouteGizmo(WayPoints wayPoints, GizmoType gizmoType)
    {
        Transform root = wayPoints.transform;
        int count = root.childCount;
        if (count == 0)
        {
            return;
        }

        Vector3[] positions = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = root.GetChild(i).position;
        }

        float minLimit;
        float maxLimit;
        bool hasRange = GetSpeedLimitRange(wayPoints, out minLimit, out maxLimit);

        for (int i = 0; i < count - 1; i++)
        {
            Color color = SegmentColor(wayPoints.GetSpeedLimit(i + 1), hasRange, minLimit, maxLimit, wayPoints.kind);
            Gizmos.color = color;
            Gizmos.DrawLine(positions[i], positions[i + 1]);
            DrawGizmoArrow(positions[i], positions[i + 1], color);
        }

        Gizmos.color = wayPoints.kind == WayPoints.RouteKind.Pedestrian ? PedestrianColor : VehicleColor;
        for (int i = 0; i < count; i++)
        {
            Gizmos.DrawWireSphere(positions[i], 0.35f);
        }
    }

    private static void DrawGizmoArrow(Vector3 from, Vector3 to, Color color)
    {
        Vector3 direction = to - from;
        if (direction.sqrMagnitude < 1e-6f)
        {
            return;
        }

        direction.Normalize();

        Vector3 side = Vector3.Cross(direction, Vector3.up);
        if (side.sqrMagnitude < 1e-6f)
        {
            side = Vector3.Cross(direction, Vector3.forward);
        }

        side.Normalize();

        Vector3 tip = Vector3.Lerp(from, to, 0.65f);
        const float length = 0.6f;
        const float spread = 0.35f;

        Gizmos.color = color;
        Gizmos.DrawLine(tip, tip - direction * length + side * spread);
        Gizmos.DrawLine(tip, tip - direction * length - side * spread);
    }

    // Lowest and highest positive segment limit on the route.
    private static bool GetSpeedLimitRange(WayPoints wayPoints, out float min, out float max)
    {
        min = float.PositiveInfinity;
        max = float.NegativeInfinity;

        int count = wayPoints.transform.childCount;
        for (int i = 1; i < count; i++)
        {
            float limit = wayPoints.GetSpeedLimit(i);
            if (limit <= 0f)
            {
                continue;
            }

            if (limit < min)
            {
                min = limit;
            }

            if (limit > max)
            {
                max = limit;
            }
        }

        return max >= min;
    }

    private static Color SegmentColor(float limit, bool hasRange, float min, float max, WayPoints.RouteKind kind)
    {
        if (!s_ColorBySpeedLimit || !hasRange)
        {
            return kind == WayPoints.RouteKind.Pedestrian ? PedestrianColor : VehicleColor;
        }

        if (limit <= 0f)
        {
            return UnlimitedColor;
        }

        float t = max > min ? Mathf.InverseLerp(min, max, limit) : 1f;
        if (t < 0.5f)
        {
            return Color.Lerp(LowLimitColor, MidLimitColor, t * 2f);
        }

        return Color.Lerp(MidLimitColor, HighLimitColor, (t - 0.5f) * 2f);
    }

    private void ValidateInto(WayPoints wayPoints, List<ValidationMessage> results)
    {
        results.Clear();

        Transform root = wayPoints.transform;
        int count = root.childCount;

        if (count < 2)
        {
            results.Add(new ValidationMessage(true, "A route needs at least 2 waypoints.", root.position));
        }

        for (int i = 0; i < count; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
            {
                results.Add(new ValidationMessage(true, string.Format("Waypoint {0} is missing.", i), root.position));
                continue;
            }

            if (i > 0)
            {
                float distance = Vector3.Distance(root.GetChild(i - 1).position, child.position);
                if (distance < MinSegmentLength)
                {
                    results.Add(new ValidationMessage(true, string.Format("Waypoints {0} and {1} overlap (distance {2:0.###}).", i - 1, i, distance), child.position));
                }
                else if (distance < RecommendedSpacing)
                {
                    results.Add(new ValidationMessage(false, string.Format("Waypoints {0} and {1} are very close ({2:0.##} m).", i - 1, i, distance), child.position));
                }
            }
        }

        for (int i = 1; i < count - 1; i++)
        {
            Vector3 previous = root.GetChild(i).position - root.GetChild(i - 1).position;
            Vector3 next = root.GetChild(i + 1).position - root.GetChild(i).position;
            if (previous.sqrMagnitude < 1e-6f || next.sqrMagnitude < 1e-6f)
            {
                continue;
            }

            float turn = Vector3.Angle(previous, next);
            if (turn >= ReversalAngle)
            {
                results.Add(new ValidationMessage(false, string.Format("Waypoint {0} reverses direction ({1:0}°).", i, turn), root.GetChild(i).position));
            }
            else if (turn >= SharpTurnAngle)
            {
                results.Add(new ValidationMessage(false, string.Format("Waypoint {0} is a sharp turn ({1:0}°).", i, turn), root.GetChild(i).position));
            }
        }

        if (wayPoints.segmentSpeedLimits != null)
        {
            if (wayPoints.segmentSpeedLimits.Length != count)
            {
                results.Add(new ValidationMessage(false, string.Format("segmentSpeedLimits has {0} entries but the route has {1} waypoints.", wayPoints.segmentSpeedLimits.Length, count), root.position));
            }

            for (int i = 0; i < wayPoints.segmentSpeedLimits.Length; i++)
            {
                if (wayPoints.segmentSpeedLimits[i] < 0f)
                {
                    results.Add(new ValidationMessage(true, string.Format("Segment limit {0} is negative.", i), root.position));
                }
            }
        }

        ValidateAgainstOtherRoutes(wayPoints, results);
    }

    private void ValidateAgainstOtherRoutes(WayPoints wayPoints, List<ValidationMessage> results)
    {
        Transform root = wayPoints.transform;
        int count = root.childCount;
        if (count < 2)
        {
            return;
        }

        WayPoints[] allRoutes = Object.FindObjectsOfType<WayPoints>();
        for (int r = 0; r < allRoutes.Length; r++)
        {
            WayPoints other = allRoutes[r];
            if (other == null || other == wayPoints || other.transform.childCount < 2)
            {
                continue;
            }

            Transform otherRoot = other.transform;
            bool differentKind = other.kind != wayPoints.kind;
            float threshold = differentKind ? CrossingDistance : LaneOverlapDistance;

            for (int i = 0; i < count - 1; i++)
            {
                Vector3 a1 = root.GetChild(i).position;
                Vector3 a2 = root.GetChild(i + 1).position;

                for (int j = 0; j < otherRoot.childCount - 1; j++)
                {
                    Vector3 b1 = otherRoot.GetChild(j).position;
                    Vector3 b2 = otherRoot.GetChild(j + 1).position;

                    float sqrDistance = SqrDistanceSegmentSegment(a1, a2, b1, b2);
                    if (sqrDistance > threshold * threshold)
                    {
                        continue;
                    }

                    Vector3 midpoint = (a1 + a2 + b1 + b2) * 0.25f;

                    if (differentKind)
                    {
                        results.Add(new ValidationMessage(true, string.Format("This route crosses the {0} route '{1}' (segments {2}/{3}).", other.kind, other.name, i, j), midpoint));
                    }
                    else if (AreParallel(a2 - a1, b2 - b1))
                    {
                        results.Add(new ValidationMessage(false, string.Format("Lanes overlap with route '{0}' (segments {1}/{2}).", other.name, i, j), midpoint));
                    }
                }
            }
        }
    }

    private static bool AreParallel(Vector3 a, Vector3 b)
    {
        if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f)
        {
            return false;
        }

        return Mathf.Abs(Vector3.Dot(a.normalized, b.normalized)) > 0.95f;
    }

    // Standard closest-distance-between-two-segments (Ericson, Real-Time Collision Detection).
    private static float SqrDistanceSegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        Vector3 d1 = q1 - p1;
        Vector3 d2 = q2 - p2;
        Vector3 r = p1 - p2;

        float a = Vector3.Dot(d1, d1);
        float e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);

        float s;
        float t;

        if (a <= 1e-8f && e <= 1e-8f)
        {
            return (p1 - p2).sqrMagnitude;
        }

        if (a <= 1e-8f)
        {
            s = 0f;
            t = Mathf.Clamp01(f / e);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= 1e-8f)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denominator = a * e - b * b;
                s = denominator > 1e-8f ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;

                t = (b * s + f) / e;
                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((b - c) / a);
                }
            }
        }

        Vector3 closest1 = p1 + d1 * s;
        Vector3 closest2 = p2 + d2 * t;
        return (closest1 - closest2).sqrMagnitude;
    }

    private float RouteLength(Transform root)
    {
        float length = 0f;
        for (int i = 1; i < root.childCount; i++)
        {
            length += Vector3.Distance(root.GetChild(i - 1).position, root.GetChild(i).position);
        }

        return length;
    }

    private bool HasSelectableChild(Transform root)
    {
        for (int i = 0; i < Selection.transforms.Length; i++)
        {
            if (Selection.transforms[i].parent == root)
            {
                return true;
            }
        }

        return false;
    }

    private void AddWaypoint(Transform root)
    {
        Vector3 position = root.position;
        if (root.childCount > 0)
        {
            Transform last = root.GetChild(root.childCount - 1);
            Vector3 direction = root.childCount > 1
                ? (last.position - root.GetChild(root.childCount - 2).position).normalized
                : root.forward;
            position = last.position + direction * 5f;
        }

        CreateWaypoint(root, root.childCount, position);
    }

    private void InsertAfterSelection(Transform root)
    {
        for (int i = 0; i < Selection.transforms.Length; i++)
        {
            Transform selected = Selection.transforms[i];
            if (selected.parent != root)
            {
                continue;
            }

            int index = selected.GetSiblingIndex();
            Transform next = index + 1 < root.childCount ? root.GetChild(index + 1) : null;
            Vector3 position = next != null
                ? (selected.position + next.position) * 0.5f
                : selected.position + root.forward * 5f;

            CreateWaypoint(root, index + 1, position);
        }
    }

    private void CreateWaypoint(Transform root, int index, Vector3 position)
    {
        GameObject point = new GameObject("Waypoint");
        Undo.RegisterCreatedObjectUndo(point, "Add Waypoint");
        Undo.SetTransformParent(point.transform, root, "Add Waypoint");

        point.transform.position = position;
        point.transform.SetSiblingIndex(Mathf.Clamp(index, 0, root.childCount - 1));

        Selection.activeGameObject = point;
        MarkDirty(point);
    }

    private void RemoveWaypoint(Transform root, int index)
    {
        if (index < 0 || index >= root.childCount)
        {
            return;
        }

        GameObject point = root.GetChild(index).gameObject;
        Undo.DestroyObjectImmediate(point);
        MarkDirty(root.gameObject);
    }

    private void ReverseRoute(Transform root)
    {
        int count = root.childCount;
        if (count < 2)
        {
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Reverse Route");

        List<Transform> children = new List<Transform>(count);
        for (int i = 0; i < count; i++)
        {
            children.Add(root.GetChild(i));
        }

        for (int i = 0; i < count; i++)
        {
            children[i].SetSiblingIndex(count - 1 - i);
        }

        MarkDirty(root.gameObject);
    }

    private void RenameWaypoints(Transform root)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            Undo.RecordObject(child.gameObject, "Rename Waypoints");
            child.name = root.name + " Waypoint " + i;
        }

        MarkDirty(root.gameObject);
    }

    private void FlattenY(Transform root)
    {
        if (root.childCount == 0)
        {
            return;
        }

        float y = root.GetChild(0).position.y;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            Undo.RecordObject(child, "Flatten Route");
            child.position = new Vector3(child.position.x, y, child.position.z);
        }

        MarkDirty(root.gameObject);
    }

    private void FitSpeedLimits(Transform root)
    {
        SerializedProperty limits = serializedObject.FindProperty("segmentSpeedLimits");
        if (limits == null)
        {
            return;
        }

        limits.arraySize = root.childCount;
        serializedObject.ApplyModifiedProperties();
        MarkDirty(root.gameObject);
    }

    private static void MarkDirty(GameObject gameObject)
    {
        if (gameObject != null && gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
    }
}
#endif
