using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Command-line-only collision stress probe for the built player.
/// Run with -sweepCollisionStress; normal gameplay never creates this component.
/// </summary>
public sealed class SweepCollisionStressProbe : MonoBehaviour
{
    private const string Argument = "-sweepCollisionStress";
    private const string Prefix = "COLLISION_STRESS_";
    private const int TargetCount = 12;
    private static bool _bootstrapped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_bootstrapped || !HasCommandLineArgument(Argument)) return;
        _bootstrapped = true;
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (SceneManager.GetActiveScene().name == "Sweep") AttachProbe();
        else SceneManager.LoadScene("Sweep");
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Sweep") AttachProbe();
    }

    private static void AttachProbe()
    {
        if (FindAnyObjectByType<SweepCollisionStressProbe>() != null) return;
        new GameObject(nameof(SweepCollisionStressProbe)).AddComponent<SweepCollisionStressProbe>();
    }

    private static bool HasCommandLineArgument(string expected)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length; i++)
        {
            if (string.Equals(arguments[i], expected, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private IEnumerator Start()
    {
        float deadline = Time.realtimeSinceStartup + 8f;
        while ((TrashPool.Instance == null || TrashPool.Instance.ActiveTrashList.Count < TargetCount ||
                WorldBounds2D.Instance == null || SpatialGridManager.Instance == null) &&
               Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        if (TrashPool.Instance == null || WorldBounds2D.Instance == null || SpatialGridManager.Instance == null)
        {
            Fail("scene systems did not initialize within 8 seconds");
            yield break;
        }

        DisableUnrelatedSimulation();
        List<BaseTrash> staged = StageDenseFastCluster();
        if (staged.Count < TargetCount)
        {
            Fail($"only {staged.Count}/{TargetCount} usable trash objects were available");
            yield break;
        }

        Vector2[] startingPositions = new Vector2[staged.Count];
        for (int i = 0; i < staged.Count; i++) startingPositions[i] = staged[i].transform.position;
        float initialPenetration = GetMaxPairPenetration(staged);
        float peakSpeed = 0f;
        float maxTravel = 0f;
        float settleDeadline = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < settleDeadline)
        {
            for (int i = 0; i < staged.Count; i++)
            {
                BaseTrash current = staged[i];
                if (current == null) continue;
                peakSpeed = Mathf.Max(peakSpeed, current.CurrentVelocity.magnitude);
                maxTravel = Mathf.Max(maxTravel,
                    Vector2.Distance(startingPositions[i], current.transform.position));
            }
            yield return null;
        }

        Audit(staged, out int finiteErrors, out int boundaryErrors, out int severePairs,
            out float finalMaxPenetration, out float finalMaxSpeed);
        bool active = staged.TrueForAll(trash => trash != null && trash.gameObject.activeInHierarchy);
        bool exercised = initialPenetration > 0.05f && peakSpeed > 0.5f && maxTravel > 0.05f;
        bool passed = active && exercised && finiteErrors == 0 && boundaryErrors == 0 && severePairs == 0;

        string result =
            $"active={active} count={staged.Count} exercised={exercised} initialPen={initialPenetration:F3} " +
            $"peakSpeed={peakSpeed:F3} maxTravel={maxTravel:F3} finiteErrors={finiteErrors} " +
            $"boundaryErrors={boundaryErrors} severePairs={severePairs} " +
            $"finalMaxPen={finalMaxPenetration:F3} finalMaxSpeed={finalMaxSpeed:F3}";
        if (!passed)
        {
            Fail(result);
            yield break;
        }

        Debug.Log($"[SweepCollisionStress] SWEEP_COLLISION_STRESS_OK {result}");
        Application.Quit(0);
    }

    private static void DisableUnrelatedSimulation()
    {
        Time.timeScale = 1f;
        BlackHoleObstacle[] holes = FindObjectsByType<BlackHoleObstacle>(FindObjectsInactive.Include);
        for (int i = 0; i < holes.Length; i++)
        {
            Collider2D[] colliders = holes[i].GetComponentsInChildren<Collider2D>(true);
            for (int c = 0; c < colliders.Length; c++) colliders[c].enabled = false;

            PetAI ai = holes[i].GetComponentInParent<PetAI>();
            if (ai == null) continue;
            ai.enabled = false;
            Rigidbody2D body = ai.GetComponent<Rigidbody2D>();
            if (body != null) body.linearVelocity = Vector2.zero;
        }

        if (DynamicSpawnManager.Instance != null)
        {
            DynamicSpawnManager.Instance.StopAllCoroutines();
            DynamicSpawnManager.Instance.enabled = false;
        }
        if (PlayerController.instance != null)
        {
            PlayerController.instance.enabled = false;
            PlayerController.instance.transform.position = new Vector3(-30f, 10f, 0f);
        }
    }

    private static List<BaseTrash> StageDenseFastCluster()
    {
        List<BaseTrash> result = new List<BaseTrash>(TargetCount);
        Vector2 center = WorldBounds2D.Instance.GetCenter();
        List<BaseTrash> active = TrashPool.Instance.ActiveTrashList;

        for (int source = 0; source < active.Count && result.Count < TargetCount; source++)
        {
            BaseTrash trash = active[source];
            if (trash == null || trash.IsAbsorbing || trash._isStuck) continue;

            int index = result.Count;
            trash.name = Prefix + index;
            trash.transform.position = center + new Vector2((index % 4) * 0.18f, (index / 4) * 0.18f);
            float angle = index / (float)TargetCount * Mathf.PI * 2f;
            trash.ApplyExternalImpulse(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 8f);
            result.Add(trash);
        }

        return result;
    }

    private static void Audit(
        IReadOnlyList<BaseTrash> trash,
        out int finiteErrors,
        out int boundaryErrors,
        out int severePairs,
        out float maxPenetration,
        out float maxSpeed)
    {
        finiteErrors = 0;
        boundaryErrors = 0;
        severePairs = 0;
        maxPenetration = 0f;
        maxSpeed = 0f;
        for (int i = 0; i < trash.Count; i++)
        {
            BaseTrash current = trash[i];
            if (current == null || !current.gameObject.activeInHierarchy)
            {
                finiteErrors++;
                continue;
            }

            Vector2 position = current.transform.position;
            if (!IsFinite(position) || !IsFinite(current.CurrentVelocity)) finiteErrors++;
            maxSpeed = Mathf.Max(maxSpeed, current.CurrentVelocity.magnitude);

            for (int nodeIndex = 0; nodeIndex < current.collisionNodes.Length; nodeIndex++)
            {
                Vector2 nodeCenter = position + current.collisionNodes[nodeIndex];
                float padding = Mathf.Max(0f, current.collisionCheckRadius - 0.02f);
                bool invalid = false;
                for (int boundIndex = 0; boundIndex < WorldBounds2D.ActiveBounds.Count; boundIndex++)
                {
                    WorldBounds2D boundary = WorldBounds2D.ActiveBounds[boundIndex];
                    if (boundary == null || !boundary.isActiveAndEnabled ||
                        !boundary.IsOutside(nodeCenter, padding)) continue;
                    invalid = true;
                    break;
                }

                if (!invalid) continue;
                boundaryErrors++;
                break;
            }

            for (int otherIndex = i + 1; otherIndex < trash.Count; otherIndex++)
            {
                BaseTrash other = trash[otherIndex];
                if (other == null || !other.gameObject.activeInHierarchy) continue;
                float pairPenetration = GetDeepestPenetration(current, other);
                maxPenetration = Mathf.Max(maxPenetration, pairPenetration);
                if (pairPenetration > Mathf.Min(
                        current.collisionCheckRadius,
                        other.collisionCheckRadius) * 0.35f)
                {
                    severePairs++;
                }
            }
        }
    }

    private static float GetDeepestPenetration(BaseTrash first, BaseTrash second)
    {
        float deepest = 0f;
        Vector2 firstPosition = first.transform.position;
        Vector2 secondPosition = second.transform.position;
        float combinedRadius = first.collisionCheckRadius + second.collisionCheckRadius;

        for (int a = 0; a < first.collisionNodes.Length; a++)
        {
            for (int b = 0; b < second.collisionNodes.Length; b++)
            {
                float penetration = combinedRadius - Vector2.Distance(
                    firstPosition + first.collisionNodes[a],
                    secondPosition + second.collisionNodes[b]);
                deepest = Mathf.Max(deepest, penetration);
            }
        }
        return deepest;
    }

    private static float GetMaxPairPenetration(IReadOnlyList<BaseTrash> trash)
    {
        float deepest = 0f;
        for (int i = 0; i < trash.Count; i++)
        {
            if (trash[i] == null) continue;
            for (int j = i + 1; j < trash.Count; j++)
            {
                if (trash[j] == null) continue;
                deepest = Mathf.Max(deepest, GetDeepestPenetration(trash[i], trash[j]));
            }
        }
        return deepest;
    }

    private static bool IsFinite(Vector2 value)
    {
        return !float.IsNaN(value.x) && !float.IsNaN(value.y) &&
               !float.IsInfinity(value.x) && !float.IsInfinity(value.y);
    }

    private static void Fail(string reason)
    {
        Debug.LogError($"[SweepCollisionStress] SWEEP_COLLISION_STRESS_FAILED {reason}");
        Application.Quit(2);
    }
}
