using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerLook : MonoBehaviour
{

    private float pitch;
    
    [SerializeField]
    private PlayerInputReader inputReader;
    [SerializeField]
    private float lookSensitivity;
    [SerializeField]
    private Transform cameraTransform;
    [SerializeField]
    private float maxPitch;
    
    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        LookLogic();
    }
    
    private void LookLogic()
    {

        Vector2 look = inputReader.LookDirection;
        
        pitch -= look.y * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
        
        transform.Rotate(0, look.x * lookSensitivity, 0);
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0, 0);
        
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
    }
    
    
}