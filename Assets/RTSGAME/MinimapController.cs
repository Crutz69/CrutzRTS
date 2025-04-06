// MinimapController.cs
using UnityEngine;
using UnityEngine.EventSystems; // Required for event handling interfaces
using UnityEngine.UI; // Required for RawImage

[RequireComponent(typeof(RawImage))] // Ensure RawImage is present
public class MinimapController : MonoBehaviour, IPointerClickHandler // Implement click handler interface
{
    private RectTransform minimapRectTransform;
    private RawImage minimapImage;

    // Store map limits - these could be set manually or retrieved from RTSCameraController
    // Ensure these values MATCH the Pan Limits in RTSCameraController
    public Vector2 mapWorldSizeX = new Vector2(-50f, 50f);
    public Vector2 mapWorldSizeZ = new Vector2(-50f, 50f);

    void Awake()
    {
        minimapRectTransform = this.GetComponent<RectTransform>();
        // ... setup rectTransform and image ...

        if (RTSCameraController.Instance != null) // Check if the Instance exists
        {
            mapWorldSizeX = RTSCameraController.Instance.panLimitX;
            mapWorldSizeZ = RTSCameraController.Instance.panLimitZ;
        }
        else // If Instance is null, this code runs:
        {
            Debug.LogError("MinimapController could not find RTSCameraController instance to get limits!");
            // Use default/inspector values as fallback
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Check if the RTSCameraController exists
        if (RTSCameraController.Instance == null)
        {
            Debug.LogError("RTSCameraController instance not found for teleport!");
            return;
        }

        Vector2 localClickPoint;

        // Convert screen click position to local position within the RawImage RectTransform
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                minimapRectTransform,
                eventData.position, // The screen position of the click
                eventData.pressEventCamera, // The camera associated with the Canvas (usually Main Camera for Screen Space Overlay)
                out localClickPoint))
        {
            // --- Coordinate Normalization (0 to 1 range) ---
            // Adjust the local point based on the pivot. Assuming pivot is center (0.5, 0.5) by default for RawImage
            // We need to shift the local point so (0,0) is the bottom-left corner *of the rect* before normalizing.
            Rect rect = minimapRectTransform.rect;
            float pivotOffsetX = rect.width * minimapRectTransform.pivot.x;
            float pivotOffsetY = rect.height * minimapRectTransform.pivot.y;

            // Shifted position relative to bottom-left corner
            float shiftedX = localClickPoint.x + pivotOffsetX;
            float shiftedY = localClickPoint.y + pivotOffsetY;

            // Normalize coordinates (0 to 1)
            float normalizedX = Mathf.Clamp01(shiftedX / rect.width);
            float normalizedY = Mathf.Clamp01(shiftedY / rect.height);

            // --- Map Normalized Coords to World Coords ---
            float worldX = Mathf.Lerp(mapWorldSizeX.x, mapWorldSizeX.y, normalizedX);
            float worldZ = Mathf.Lerp(mapWorldSizeZ.x, mapWorldSizeZ.y, normalizedY);

            Vector3 targetWorldXZ = new Vector3(worldX, 0, worldZ); // Y component doesn't matter here

            // --- Tell Camera Controller to Teleport ---
            RTSCameraController.Instance.TeleportTo(targetWorldXZ);
        }
        else
        {
            Debug.LogWarning("Could not convert screen point to local point on minimap.");
        }
    }


}