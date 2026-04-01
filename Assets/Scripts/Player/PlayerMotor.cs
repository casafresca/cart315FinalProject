using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerMotor : MonoBehaviour
{
    private CharacterController controller;
    private Vector3 playerVelocity;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float fallTimer;

    public float speed = 5.0f;
    private bool isGrounded;
    public float gravity = -9.81f;
    public float jumpHeight = 3.0f;
    public float maxFallDuration = 5.0f;

    public bool crouching = false;
    public float crouchTimer = 1;
    public bool lerpCrouch = false;
    public bool sprinting = false;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        controller = GetComponent<CharacterController>();
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;

        if (TherapySessionState.TryConsumeReturnPoint(SceneManager.GetActiveScene().name, out Vector3 returnPosition, out Quaternion returnRotation))
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(returnPosition, returnRotation);
            controller.enabled = true;

            spawnPosition = returnPosition;
            spawnRotation = returnRotation;

            Debug.Log("Player returned from therapy room to saved position: " + returnPosition);
        }
    }

    // Update is called once per frame
    void Update()
    {
        isGrounded = controller.isGrounded;
        UpdateFallTimer();

        if (lerpCrouch)
        {
            crouchTimer += Time.deltaTime;
            float p = crouchTimer / 1;
            p *= p;
            if (crouching)
            {
                controller.height = Mathf.Lerp(controller.height, 1, p);
            }
            else
            {
                controller.height = Mathf.Lerp(controller.height, 2, p);
            }

            if (p > 1)
            {
                lerpCrouch = false;
                crouchTimer = 0;
            }
        }
    }

    private void UpdateFallTimer()
    {
        if (isGrounded)
        {
            fallTimer = 0f;
            return;
        }

        if (playerVelocity.y < 0f)
        {
            fallTimer += Time.deltaTime;

            if (fallTimer >= maxFallDuration)
            {
                RespawnAtSpawnPoint();
            }
        }
        else
        {
            fallTimer = 0f;
        }
    }

    private void RespawnAtSpawnPoint()
    {
        fallTimer = 0f;
        playerVelocity = Vector3.zero;
        controller.enabled = false;
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        controller.enabled = true;
    }

    public void ProcessMove(Vector2 input)
        {
            Vector3 moveDirection = Vector3.zero;
            moveDirection.x = input.x;
            moveDirection.z = input.y;
            controller.Move(transform.TransformDirection(moveDirection) * speed * Time.deltaTime);
            playerVelocity.y += gravity * Time.deltaTime;
            if(isGrounded && playerVelocity.y < 0)
            {
                playerVelocity.y = -2f;
            }
            controller.Move(playerVelocity * Time.deltaTime);
    }

    public void Jump()
    {
        if(isGrounded)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -3f * gravity);
        }
    }

    public void Crouch()
    {
        crouching = !crouching;
        crouchTimer = 0;
        lerpCrouch = true;
    }
     public void Sprint()
    {
        sprinting = !sprinting;
        if (sprinting)
        {
            speed = 10f;
        }
        else
        {
            speed = 5f;
        }
    }
}
