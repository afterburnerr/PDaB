using UnityEngine;
using UnityEngine.Serialization;

public class PlayerMotor : MonoBehaviour
{
    
    private CharacterController controller;
    private bool isSprinting;

    public bool IsSprinting => isSprinting;
    
    [SerializeField]
    private PlayerInputReader inputReader;
    [SerializeField, FormerlySerializedAs("playerStamina")]
    private PlayerAuxPower auxPower;
    [SerializeField] 
    private float basicSpeed;
    [SerializeField]
    private float sprintSpeed;
    
    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        
        SprintLogic();
        MoveLogic();
        
    }

    private void SprintLogic()
    {
        
        if (
            inputReader.IsSprintActive &&
            inputReader.MoveDirection.sqrMagnitude > 0 &&
            auxPower.CanSprint
            ) isSprinting = true;
        else isSprinting = false;
        
        auxPower.Tick(isSprinting, Time.deltaTime);
        
    }
    
    private void MoveLogic()
    {
        Vector2 input = inputReader.MoveDirection;
        Vector3 direction = transform.right * input.x + transform.forward * input.y;

        float speed = basicSpeed;
        
        if (isSprinting) speed = sprintSpeed;
        controller.Move(direction * (speed * Time.deltaTime));
    }
    
}
