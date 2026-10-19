using UnityEngine;
using UnityEngine.Serialization;

public class PlayerAuxPower : MonoBehaviour
{
    
    private float power;
    private float regenTimer;
    private bool exhausted;
    private bool flashlightActive;
    
    public float CurrentPower => power;
    public float NormalizedPower => maxPower > 0f ? power / maxPower : 0f;
    public bool CanSprint => !exhausted && power > 0;
    public bool IsSprintExhausted => exhausted;

    [SerializeField, FormerlySerializedAs("maxStamina"), Min(1f)]
    private float maxPower = 100f;
    [SerializeField, FormerlySerializedAs("drainPerSecond"), Min(0f)]
    private float sprintDrainPerSecond = 20f;
    [SerializeField, Min(0f)]
    private float flashlightDrainPerSecond = 2.222222f;
    [SerializeField]
    private float regenPerSecond;
    [SerializeField]
    private float regenDelay;
    [SerializeField]
    private float resumeThreshold;

    void Awake()
    {
        power = maxPower;
    }

    public void SetFlashlightActive(bool active)
    {
        flashlightActive = active;
        if (active) regenTimer = regenDelay;
    }

    public void Tick(bool isSprinting, float deltaTime)
    {

        float drain = (isSprinting ? sprintDrainPerSecond : 0f)
            + (flashlightActive ? flashlightDrainPerSecond : 0f);
        if (drain > 0f)
        {
            power -= drain * deltaTime;
            regenTimer = regenDelay;
        }
        else
        {
            regenTimer -= deltaTime;

            if (regenTimer <= 0)
            {
                power += regenPerSecond * deltaTime;
            }
        }
        
        power = Mathf.Clamp(power, 0, maxPower);
        
        if (power <= 0) exhausted = true;
        if (exhausted && power >= resumeThreshold) exhausted = false;
        
    }
    
    
}
