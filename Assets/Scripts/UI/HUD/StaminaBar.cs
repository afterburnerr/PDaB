using UnityEngine;
using UnityEngine.UI;

namespace FPLab.UI
{
    public class StaminaBar : MonoBehaviour
    {
        
        [SerializeField] 
        private PlayerAuxPower playerStamina;
        [SerializeField]
        private Image fillImage;

        void LateUpdate()
        {
            fillImage.fillAmount = playerStamina.NormalizedPower;
        }
        
    }
}
