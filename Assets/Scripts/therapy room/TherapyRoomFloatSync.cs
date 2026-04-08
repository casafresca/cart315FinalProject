using UnityEngine;

public class TherapyRoomFloatSync : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("Assign the root that holds all the floating blocks. If empty, uses this transform.")]
    [SerializeField] private Transform blocksRoot;

    [Header("Motion")]
    [SerializeField] private float amplitude = 0.25f;
    [SerializeField] private float frequency = 0.6f;
    [SerializeField] private bool useUnscaledTime = true;

    private Transform[] targets = new Transform[0];
    private Vector3[] baseLocalPositions = new Vector3[0];

    private void Awake()
    {
        Transform root = blocksRoot != null ? blocksRoot : transform;
        targets = root.GetComponentsInChildren<Transform>(true);

        baseLocalPositions = new Vector3[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            baseLocalPositions[i] = targets[i].localPosition;
        }
    }

    private void Update()
    {
        float time = useUnscaledTime ? Time.unscaledTime : Time.time;
        float offsetY = Mathf.Sin(time * Mathf.PI * 2f * frequency) * amplitude;

        for (int i = 0; i < targets.Length; i++)
        {
            Transform t = targets[i];
            if (t == null)
            {
                continue;
            }

            Vector3 basePos = baseLocalPositions[i];
            t.localPosition = new Vector3(basePos.x, basePos.y + offsetY, basePos.z);
        }
    }
}
