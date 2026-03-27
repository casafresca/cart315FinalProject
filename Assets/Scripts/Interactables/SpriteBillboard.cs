using UnityEngine;

/// <summary>
/// Makes a sprite face the camera in a 3D scene.
/// Put this on the sprite visual child object (not the NPC root).
/// </summary>
public class SpriteBillboard : MonoBehaviour
{
    [Header("Camera")]
    [Tooltip("Optional camera override. If empty, uses Camera.main.")]
    [SerializeField] private Camera targetCamera;

    [Header("Rotation Tuning")]
    [Tooltip("Extra Y rotation offset so you can quickly fix front/back orientation.")]
    [SerializeField] private float yRotationOffset = 180f;

    [Tooltip("If true, lock rotation to Y-only so sprite stays upright.")]
    [SerializeField] private bool yAxisOnly = true;

    private void LateUpdate()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
        {
            return;
        }

        if (yAxisOnly)
        {
            // Horizontal billboard: face camera direction while staying vertical.
            Vector3 lookDirection = cam.transform.position - transform.position;
            lookDirection.y = 0f;

            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion baseRotation = Quaternion.LookRotation(-lookDirection.normalized, Vector3.up);
                transform.rotation = baseRotation * Quaternion.Euler(0f, yRotationOffset, 0f);
            }
        }
        else
        {
            // Full billboard: copy camera facing directly.
            transform.rotation = cam.transform.rotation * Quaternion.Euler(0f, yRotationOffset, 0f);
        }
    }
}
