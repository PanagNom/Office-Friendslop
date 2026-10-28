using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
public class PlayerCameraController : NetworkBehaviour
{
    private const string LookActionName = "Look";

    [SerializeField]
    private GameObject playerCamera;

    [SerializeField]
    [Range(0.01f, 1f)]
    private float lookSensitivity = 0.1f;

    [SerializeField]
    [Range(0f, 90f)]
    private float maxPitch = 85f;

    private GameObject sceneCamera;
    private InputAction lookAction;
    private float pitch;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            return;

        // Hand over from a scene/menu camera, if there is one, so only one camera and AudioListener stay active
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            sceneCamera = mainCamera.gameObject;
            sceneCamera.SetActive(false);
        }

        playerCamera.SetActive(true);
        lookAction = GetComponent<PlayerInput>().actions[LookActionName];
        SetCursorLocked(true);
    }

    public override void OnNetworkDespawn()
    {
        playerCamera.SetActive(false);

        if (sceneCamera != null)
            sceneCamera.SetActive(true);

        // Only the local player's instance has a look action, so remote players leaving never unlock our cursor
        if (lookAction != null)
        {
            lookAction = null;
            SetCursorLocked(false);
        }
    }

    private void Update()
    {
        if (lookAction == null)
            return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetCursorLocked(false);
        else if (Cursor.lockState != CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            SetCursorLocked(true);

        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        // Mouse delta is already per frame, so it is not scaled by Time.deltaTime
        Vector2 look = lookAction.ReadValue<Vector2>() * lookSensitivity;

        // Yaw turns the whole player (synced by NetworkTransform), pitch only tilts the local camera
        transform.Rotate(0f, look.x, 0f);
        pitch = Mathf.Clamp(pitch - look.y, -maxPitch, maxPitch);
        playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
