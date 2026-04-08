using UnityEngine;

public class WeaponToggle : MonoBehaviour
{
    [Header("Weapon Reference")]
    [Tooltip("Drag the Dummy Weapon here.")]
    public GameObject weaponToToggle;

    void Start()
    {
        // Ensures the player starts the game with no weapon equipped
        if (weaponToToggle != null)
        {
            weaponToToggle.SetActive(false);
        }
        else
        {
            Debug.LogWarning("WeaponToggle: No weapon assigned in the inspector!");
        }
    }

    void Update()
    {
        // Listens for the 'Q' key being pressed down
        if (Input.GetKeyDown(KeyCode.Q))
        {
            if (weaponToToggle != null)
            {
                // Checks the current state and sets it to the opposite
                bool isCurrentlyActive = weaponToToggle.activeSelf;
                weaponToToggle.SetActive(!isCurrentlyActive);
            }
        }
    }
}
