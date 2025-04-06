// RTSCameraController.cs (Additions/Modifications marked with ***)
using UnityEngine;

public class RTSCameraController : MonoBehaviour
{
    [Header("Movement Speeds")]
    public float panSpeed = 20f;
    public float freeLookMoveSpeed = 15f;
    public float scrollSpeed = 20f;
    public float rotationSpeed = 50f;
    public float freeLookRotationSpeed = 2.5f;
    public float sprintMultiplier = 2.0f; // *** How much faster to move when holding Shift

    [Header("Panning & Limits")]
    public float panBorderThickness = 15f;
    public Vector2 panLimitX = new Vector2(-50f, 50f);
    public Vector2 panLimitZ = new Vector2(-50f, 50f);
    public bool enableMapLimits = true;

    [Header("Zooming & Height")]
    public float minY = 5f;
    public float maxY = 50f;

    [Header("Rotation & Free Look")]
    public bool allowNormalRotation = true;
    public float freeLookPitchMin = -85f;
    public float freeLookPitchMax = 85f;

    private bool isFreeLookActive = false;
    private float currentYaw = 0f;
    private float currentPitch = 0f;

    // *** Static instance for easy access from Minimap (Simple Singleton)
    public static RTSCameraController Instance;

    void Awake() // *** Use Awake for Singleton initialization
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Debug.LogWarning("Multiple RTSCameraController instances detected. Destroying duplicate.");
            Destroy(gameObject);
        }
    }


    void Start()
    {
        Vector3 initialAngles = transform.eulerAngles;
        currentYaw = initialAngles.y;
        currentPitch = initialAngles.x;
        if (currentPitch > 180f) currentPitch -= 360f;
        currentPitch = Mathf.Clamp(currentPitch, freeLookPitchMin, freeLookPitchMax);
    }

    void Update()
    {
        HandleFreeLookToggleInput();

        if (isFreeLookActive)
        {
            HandleFreeLookRotation();
            HandleFreeLookMovement(); // *** Sprint logic added inside this function
            if (enableMapLimits) ApplyPositionLimitsXZ();
        }
        else
        {
            HandleKeyboardAndEdgePanning(); // *** Sprint logic added inside this function
            HandleZooming();
            if (allowNormalRotation) HandleNormalRotation();
            if (enableMapLimits) ApplyPositionLimits();
        }
    }

    void HandleFreeLookMovement()
    {
        // *** Check for Sprint Key
        bool isSprinting = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float currentMoveSpeed = freeLookMoveSpeed * (isSprinting ? sprintMultiplier : 1f);
        // --- rest of the function ---

        float moveForward = Input.GetAxis("Vertical");
        float moveStrafe = Input.GetAxis("Horizontal");
        float moveUp = 0f;
        if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.E)) moveUp += 1f;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.Q)) moveUp -= 1f;

        Vector3 moveDirection = (transform.forward * moveForward) + (transform.right * moveStrafe) + (transform.up * moveUp);

        // *** Use currentMoveSpeed instead of freeLookMoveSpeed
        transform.position += moveDirection.normalized * currentMoveSpeed * Time.deltaTime;
    }

    void HandleKeyboardAndEdgePanning()
    {
        // *** Check for Sprint Key
        bool isSprinting = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float currentPanSpeed = panSpeed * (isSprinting ? sprintMultiplier : 1f);
        // --- rest of the function ---

        Vector3 pos = transform.position;
        Vector3 moveDirection = Vector3.zero;

        // Keyboard
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) { moveDirection += GetCameraForwardOnXZPlane(); }
        // ... (other keys S, A, D) ...
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) { moveDirection -= GetCameraForwardOnXZPlane(); }
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) { moveDirection += GetCameraRightOnXZPlane(); }
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) { moveDirection -= GetCameraRightOnXZPlane(); }


        // Screen Edge
        Vector2 mousePos = Input.mousePosition;
        if (mousePos.x >= 0 && mousePos.x <= Screen.width && mousePos.y >= 0 && mousePos.y <= Screen.height)
        {
            if (mousePos.x >= Screen.width - panBorderThickness) { moveDirection += GetCameraRightOnXZPlane(); }
            // ... (other edges) ...
            if (mousePos.x <= panBorderThickness) { moveDirection -= GetCameraRightOnXZPlane(); }
            if (mousePos.y >= Screen.height - panBorderThickness) { moveDirection += GetCameraForwardOnXZPlane(); }
            if (mousePos.y <= panBorderThickness) { moveDirection -= GetCameraForwardOnXZPlane(); }
        }

        // *** Use currentPanSpeed instead of panSpeed
        pos += moveDirection.normalized * currentPanSpeed * Time.deltaTime;
        transform.position = pos;
    }

    // *** Add public method for teleporting
    public void TeleportTo(Vector3 worldXZPoint)
    {
        Vector3 targetPosition = new Vector3(worldXZPoint.x, transform.position.y, worldXZPoint.z); // Keep current Y

        // Apply limits to the target position before setting
        if (enableMapLimits)
        {
            targetPosition.x = Mathf.Clamp(targetPosition.x, panLimitX.x, panLimitX.y);
            targetPosition.y = Mathf.Clamp(targetPosition.y, minY, maxY); // Clamp Y too, just in case
            targetPosition.z = Mathf.Clamp(targetPosition.z, panLimitZ.x, panLimitZ.y);
        }

        transform.position = targetPosition;
        Debug.Log("Teleported camera to: " + targetPosition);

        // If you were in free look, you might want to update the yaw/pitch
        // based on the new position or just keep the current orientation.
        // For simplicity, we'll just change position here.
    }


    // --- Other existing functions (HandleFreeLookToggleInput, HandleFreeLookRotation,
    // --- GetCameraForwardOnXZPlane, GetCameraRightOnXZPlane, HandleZooming,
    // --- HandleNormalRotation, ApplyPositionLimits, ApplyPositionLimitsXZ) remain the same...
    void HandleFreeLookToggleInput()
    {
        if (Input.GetMouseButtonDown(2)) // Middle Mouse Button Pressed
        {
            isFreeLookActive = true;
            Vector3 angles = transform.eulerAngles;
            currentYaw = angles.y;
            currentPitch = angles.x;
            if (currentPitch > 180f) currentPitch -= 360f;
            currentPitch = Mathf.Clamp(currentPitch, freeLookPitchMin, freeLookPitchMax);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (Input.GetMouseButtonUp(2)) // Middle Mouse Button Released
        {
            isFreeLookActive = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void HandleFreeLookRotation()
    {
        float mouseX = Input.GetAxis("Mouse X") * freeLookRotationSpeed;
        float mouseY = Input.GetAxis("Mouse Y") * freeLookRotationSpeed;
        currentYaw += mouseX;
        currentPitch -= mouseY;
        currentPitch = Mathf.Clamp(currentPitch, freeLookPitchMin, freeLookPitchMax);
        transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
    }

    Vector3 GetCameraForwardOnXZPlane()
    {
        Vector3 forward = transform.forward;
        forward.y = 0;
        return forward.normalized;
    }

    Vector3 GetCameraRightOnXZPlane()
    {
        Vector3 right = transform.right;
        right.y = 0;
        return right.normalized;
    }

    void HandleZooming()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f)
        {
            Vector3 pos = transform.position;
            pos += transform.forward * scroll * scrollSpeed * 100f * Time.deltaTime;
            transform.position = pos;
        }
    }

    void HandleNormalRotation()
    {
        float rotationInput = 0f;
        if (Input.GetKey(KeyCode.Q)) { rotationInput += 1f; }
        if (Input.GetKey(KeyCode.E)) { rotationInput -= 1f; }

        if (rotationInput != 0f)
        {
            transform.Rotate(Vector3.up, rotationInput * rotationSpeed * Time.deltaTime, Space.World);
        }
    }

    void ApplyPositionLimits()
    {
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, panLimitX.x, panLimitX.y);
        pos.y = Mathf.Clamp(pos.y, minY, maxY);
        pos.z = Mathf.Clamp(pos.z, panLimitZ.x, panLimitZ.y);
        transform.position = pos;
    }
    void ApplyPositionLimitsXZ()
    {
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, panLimitX.x, panLimitX.y);
        pos.z = Mathf.Clamp(pos.z, panLimitZ.x, panLimitZ.y);
        transform.position = pos;
    }
} // End of class