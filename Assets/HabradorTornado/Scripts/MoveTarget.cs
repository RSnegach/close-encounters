using UnityEngine;

// Moves the target the tornado is chasing. Picks goals in open terrain only —
// rejects positions blocked by mountains/rocks so the tornado doesn't phase
// through them. Speed reduced from the original 55 to match slower tornado.
public class MoveTarget : MonoBehaviour
{
    float speed = 18f;

    Vector3 goal;

    float mapHalfSize = 250f;
    float clearanceRadius = 20f;
    int maxGoalAttempts = 12;

    void Start()
    {
        goal = PickValidGoal(transform.position);
    }

    void Update()
    {
        transform.LookAt(goal);
        transform.Translate(Vector3.forward * speed * Time.deltaTime);

        if ((transform.position - goal).sqrMagnitude < 2f)
            goal = PickValidGoal(transform.position);
    }

    // Random ground position with no obstacle within clearanceRadius.
    // Falls back to a nearby offset if no clear spot is found.
    Vector3 PickValidGoal(Vector3 fromPos)
    {
        for (int i = 0; i < maxGoalAttempts; i++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(-mapHalfSize, mapHalfSize),
                0f,
                Random.Range(-mapHalfSize, mapHalfSize));

            if (IsClear(candidate))
                return candidate;
        }

        // Fallback: nudge sideways from current position
        Vector3 lateral = Random.insideUnitCircle.normalized * 60f;
        return fromPos + new Vector3(lateral.x, 0f, lateral.y);
    }

    bool IsClear(Vector3 pos)
    {
        Vector3 samplePos = pos + Vector3.up * 5f;
        Collider[] hits = Physics.OverlapSphere(samplePos, clearanceRadius, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            string n = hits[i].transform.root.name;
            // Reject spots inside mountains, big rocks, cliffs, or cactuses.
            if (n.Contains("Mountain") || n.Contains("Cliff") ||
                n.Contains("Rock") || n.Contains("Cactus") ||
                n.Contains("Boulder"))
                return false;
        }
        return true;
    }
}
