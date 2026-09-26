using System.Collections.Generic;
using UnityEngine;

public enum TrafficLightPhase
{
    Green,
    Yellow,
    Red
}

// A signal placed at an intersection. Cars approach the stop line and wait while
// the light is not green; pedestrians crossing the traffic may walk while it is red.
public class TrafficLight : MonoBehaviour
{
    [Header("Timing (seconds)")]
    public float greenDuration = 6f;
    public float yellowDuration = 2f;
    public float redDuration = 6f;
    // Shifts this light's phase so crossing directions can be complementary.
    public float startOffset = 0f;

    [Header("Stop line")]
    // Where cars stop. Falls back to this transform when empty.
    public Transform stopLine;
    // Travel direction of the cars this light controls.
    public Vector3 approachDirection = Vector3.forward;
    // Lateral half-width of the controlled lane(s).
    public float laneHalfWidth = 5f;
    // Cars further than this from the line ignore it.
    public float detectionDistance = 20f;
    // Cars stop this far before the line.
    public float stopOffset = 1.5f;

    private static readonly List<TrafficLight> lights = new List<TrafficLight>();

    public static IReadOnlyList<TrafficLight> Lights
    {
        get { return lights; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        lights.Clear();
    }

    public Vector3 StopPosition
    {
        get { return stopLine != null ? stopLine.position : transform.position; }
    }

    public Vector3 Approach
    {
        get
        {
            return approachDirection.sqrMagnitude > 0.0001f
                ? approachDirection.normalized
                : transform.forward;
        }
    }

    public float CycleDuration
    {
        get { return Mathf.Max(0.01f, greenDuration + yellowDuration + redDuration); }
    }

    public TrafficLightPhase Phase
    {
        get
        {
            float t = Mathf.Repeat(Time.time + startOffset, CycleDuration);
            if (t < greenDuration)
            {
                return TrafficLightPhase.Green;
            }

            if (t < greenDuration + yellowDuration)
            {
                return TrafficLightPhase.Yellow;
            }

            return TrafficLightPhase.Red;
        }
    }

    // Pedestrians crossing this light's traffic may walk while vehicles are stopped.
    public bool PedestriansMayCross
    {
        get { return Phase == TrafficLightPhase.Red; }
    }

    private void OnEnable()
    {
        if (!lights.Contains(this))
        {
            lights.Add(this);
        }
    }

    private void OnDisable()
    {
        lights.Remove(this);
    }

    // Returns the speed a car may use while approaching this light.
    // Green (or a car past the line / off this approach) leaves the speed unchanged.
    public float LimitSpeed(Vector3 carPosition, Vector3 carForward, float desiredSpeed)
    {
        if (Phase == TrafficLightPhase.Green)
        {
            return desiredSpeed;
        }

        Vector3 toLine = StopPosition - carPosition;
        float ahead = Vector3.Dot(toLine, Approach);
        if (ahead <= 0f || ahead > detectionDistance)
        {
            // Already past the line, or still too far away to matter.
            return desiredSpeed;
        }

        // Only cars travelling roughly along the controlled approach obey this light.
        if (Vector3.Dot(carForward, Approach) < 0.5f)
        {
            return desiredSpeed;
        }

        float lateral = Vector3.ProjectOnPlane(toLine, Approach).magnitude;
        if (lateral > laneHalfWidth)
        {
            return desiredSpeed;
        }

        float gap = ahead - stopOffset;
        if (gap <= 0f)
        {
            return 0f;
        }

        float range = Mathf.Max(0.01f, detectionDistance - stopOffset);
        float factor = Mathf.Clamp01(gap / range);
        return Mathf.Min(desiredSpeed, desiredSpeed * factor);
    }

    // Light nearest to a world position, or null when none exist.
    public static TrafficLight GetClosest(Vector3 position)
    {
        TrafficLight best = null;
        float bestSqrDistance = float.PositiveInfinity;

        for (int i = 0; i < lights.Count; i++)
        {
            TrafficLight light = lights[i];
            if (light == null)
            {
                continue;
            }

            float sqrDistance = (light.StopPosition - position).sqrMagnitude;
            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
                best = light;
            }
        }

        return best;
    }
}
