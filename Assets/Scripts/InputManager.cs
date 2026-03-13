using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{

    private PlayerInput playerInput;
    public PlayerInput.OnFootActions onFoot;

    private PlayerMotor motor;
    private PlayerLook look;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        playerInput = new PlayerInput();
        onFoot = playerInput.OnFoot;
        motor = GetComponent<PlayerMotor>();
        look = GetComponent<PlayerLook>();

        // Wrap the actions in a check for the DialogueManager state
        onFoot.Jump.performed += ctx => {
            if (!DialogueManager.GetInstance().dialogueIsPlaying) motor.Jump();
        };
        onFoot.Crouch.performed += ctx => {
            if (!DialogueManager.GetInstance().dialogueIsPlaying) motor.Crouch();
        };
        onFoot.Sprint.performed += ctx => {
            if (!DialogueManager.GetInstance().dialogueIsPlaying) motor.Sprint();
        };
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        // Only move if dialogue is not playing
        if (!DialogueManager.GetInstance().dialogueIsPlaying)
        {
            motor.ProcessMove(onFoot.Movement.ReadValue<Vector2>());
        }
        else
        {
            // Stop movement immediately when dialogue opens
            motor.ProcessMove(Vector2.zero);
        }
    }

    void LateUpdate()
    {
        look.ProcessLook(onFoot.Look.ReadValue<Vector2>());
    }

    private void OnEnable()
    {
            onFoot.Enable();
    }

    private void OnDisable()
    {
            onFoot.Disable();
    }
}
