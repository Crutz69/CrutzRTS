using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Camera cameraToLookAt;

    // Används av Unit.cs för att sätta kameran
    public void SetCameraToFace(Camera cam)
    {
        cameraToLookAt = cam;
    }

    // Start kan användas som fallback om SetCameraToFace inte anropas
    void Start()
    {
        if (cameraToLookAt == null)
        {
            cameraToLookAt = Camera.main;
            Debug.LogWarning("Billboard script did not have camera assigned, using Camera.main.", this);
        }
    }

    // Uppdatera rotationen varje frame i LateUpdate (efter kameran har rört sig)
    void LateUpdate()
    {
        if (cameraToLookAt != null)
        {
            // Få kamerans position
            Vector3 camPos = cameraToLookAt.transform.position;
            // Beräkna riktningen från canvasen till kameran
            Vector3 lookDir = camPos - transform.position;
            // Nollställ Y-rotationen om du vill att den bara ska luta upp/ner mot kameran,
            // men inte rotera runt sin egen Y-axel. För health bars är det oftast bäst att titta rakt mot.
            // lookDir.y = 0; // Ta bort denna rad om du vill att den ska luta upp/ner också.

            // Sätt rotationen så att canvasens "framåt" (ofta Z+) pekar MOT kameran
            // transform.LookAt(transform.position + cameraToLookAt.transform.rotation * Vector3.forward, cameraToLookAt.transform.rotation * Vector3.up); // Alternativ metod
            transform.LookAt(camPos);
            // Rotera 180 grader runt Y om den pekar bakåt (beroende på hur Canvas/Slider är orienterad)
            // transform.Rotate(0, 180, 0); // Testa om den visas bak-och-fram
        }
        else
        {
            // Försök hitta kameran igen om den försvann
            cameraToLookAt = Camera.main;
        }
    }
}