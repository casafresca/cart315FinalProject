using UnityEngine;

public abstract class Interactable : MonoBehaviour
{
    public string promptMessage;   
    
    public void BasseInteract()
    {
        Interact();
    }
   
    protected virtual void Interact()
    {
      
    }
}
