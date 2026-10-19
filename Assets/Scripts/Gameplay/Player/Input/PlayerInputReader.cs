using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputReader : MonoBehaviour
{
    
    public Vector2 MoveDirection => moveAction.action.ReadValue<Vector2>();
    public Vector2 LookDirection => lookAction.action.ReadValue<Vector2>();
    public bool IsSprintActive => sprintAction.action.IsPressed();
    public bool WasFlashlightTogglePressed => flashlightAction != null && flashlightAction.action.WasPressedThisFrame();

    [SerializeField]
    private InputActionReference moveAction;
    [SerializeField]
    private InputActionReference lookAction;
    [SerializeField]
    private InputActionReference sprintAction;
    [SerializeField]
    private InputActionReference flashlightAction;
    
    void OnEnable()
    {
        moveAction.action.Enable();
        lookAction.action.Enable();
        sprintAction.action.Enable();
        if (flashlightAction != null) flashlightAction.action.Enable();
    }

    void OnDisable()
    {
        moveAction.action.Disable();
        lookAction.action.Disable();
        sprintAction.action.Disable();
        if (flashlightAction != null) flashlightAction.action.Disable();
    }

}
