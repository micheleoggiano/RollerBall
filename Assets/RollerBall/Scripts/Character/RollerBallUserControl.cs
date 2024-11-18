using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Interactions;
using CustomInspector;
using Cinemachine;

public class RollerBallUserControl : MonoBehaviour
{
    private RollerBall ball;
    
    private Transform cameraTransform; // A reference to the main camera in the scenes transform
    private CinemachineBrain cinemachineBrain; // A reference to the main camera in the scenes transform
    private Vector3 camForward, camUp, camRight; // The current forward direction of the camera
    private Vector3 moveInputDirection, lookInputDirection;

    private Vector3 desiredMovementDirection;
    private Vector3 lookDirection;

    private Vector3 previousMovementDirection;

    public enum GameMode { _3D , _2DSide/*, _2DTopdown*/ }
    [SerializeField] private GameMode gameMode;

    [HorizontalLine("Testing", 5, FixedColor.Gray)]

    public Transform startingPosition;
    [Button(nameof(ResetTest), label = "Reset Test", size = Size.small)]
    [HideField] public bool debugJumpReset;

    [HorizontalLine("Jump Test", 3, FixedColor.Gray)]

    public Vector2 inputDirection;
    public Vector2 initialSpeed;
    [Button(nameof(ExecuteJumpTest), label = "Jump")]
    [HideField] public bool debugJump;

    [HorizontalLine("Dash Test", 3, FixedColor.Gray)]
    public float startingDashHeight = 1f;
    [Button(nameof(ExecuteDashTest), label = "Dash")]
    [HideField] public bool debugDash;

    void ResetTest()
    {
        moveInputDirection = Vector2.zero;
        ball.SetVelocity(Vector3.zero);
        ball.transform.position = startingPosition.position;
    }

    void ExecuteJumpTest()
    {
        moveInputDirection = inputDirection;
        ball.SetVelocity(initialSpeed);
        StartCoroutine(ball.TryJump());
    }

    void ExecuteDashTest()
    {
        ball.transform.position += startingDashHeight * Vector3.up;
        //moveInputDirection = Vector2.right;
        StartCoroutine(ball.TryDash(Vector3.right, Vector3.right));
    }


    private void Awake()
    {
        ball = GetComponentInParent<RollerBall>();
        // get the transform of the main camera
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
            cinemachineBrain = Camera.main.GetComponent<CinemachineBrain>();
        }
        else
        {
            Debug.LogWarning(
                "Warning: no main camera found. Ball needs a Camera tagged \"MainCamera\", for camera-relative controls.");
        }
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        moveInputDirection = context.ReadValue<Vector2>();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        lookInputDirection = context.ReadValue<Vector2>();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        switch (context.phase)
        {
            case InputActionPhase.Started:
                StartCoroutine(ball.TryJump());
                break;
            case InputActionPhase.Canceled:
                switch (ball.state)
                {
                    case PlayerState.Grounded:
                        //  
                        break;
                    case PlayerState.Airborne:
                        ball.EvaluateJumpCutOff();
                        break;
                    default:
                        break;
                }
                break;
        }
    }

    public void OnDash(InputAction.CallbackContext context)
    {
        switch (context.phase)
        {
            case InputActionPhase.Started:
                switch (ball.state)
                {
                    case PlayerState.Grounded:
                        break;
                    case PlayerState.Airborne:
                        switch (gameMode)
                        {
                            case GameMode._3D:
                                StartCoroutine(ball.TryDash(desiredMovementDirection, camForward));
                                break;
                            case GameMode._2DSide:
                                StartCoroutine(ball.TryDash(desiredMovementDirection, previousMovementDirection));
                                break;
                        }
                        break;
                }                
                break;
            case InputActionPhase.Canceled:
                break;
        }
    }

    public void OnSlam(InputAction.CallbackContext context)
    {
        switch (context.phase)
        {
            case InputActionPhase.Started:
                switch (ball.state)
                {
                    case PlayerState.Grounded:
                        break;
                    case PlayerState.Airborne:
                        //switch (gameMode)
                        //{
                        //    case GameMode._3D:
                        //        StartCoroutine(ball.TryDash(desiredMovementDirection, camForward));
                        //        break;
                        //    case GameMode._2DSide:
                        //        StartCoroutine(ball.TryDash(desiredMovementDirection, previousMovementDirection));
                        //        break;
                        //}
                        StartCoroutine(ball.Slam());
                        break;
                }
                break;
            case InputActionPhase.Canceled:
                break;
        }
    }


    private void Update()
    {
        var upTransform = cinemachineBrain.m_WorldUpOverride;
        if (upTransform == null)
        {
            camForward = Vector3.Scale(cameraTransform.forward, new Vector3(1, 0, 1)).normalized; //QUI
            camUp = Vector3.Scale(cameraTransform.up, new Vector3(1, 0, 1)).normalized; //QUI
            camRight = cameraTransform.right;
        }
        else
        {
            camForward = Vector3.ProjectOnPlane(cameraTransform.forward, upTransform.up).normalized;
            camUp = upTransform.up;
            camRight = cameraTransform.right;
        }
        
        switch (gameMode)
        {
            case GameMode._3D:
                desiredMovementDirection = (moveInputDirection.y * camForward + moveInputDirection.x * camRight).normalized;
                break;
            case GameMode._2DSide:
                desiredMovementDirection = (moveInputDirection.x * camRight).normalized;
                if (desiredMovementDirection != Vector3.zero) previousMovementDirection = desiredMovementDirection.normalized;
                break;
            //case GameMode._2DTopdown:
            //    desiredMovementDirection = (moveInputDirection.y * camUp +  moveInputDirection.x * camRight).normalized;
            //    if (desiredMovementDirection != Vector3.zero) previousMovementDirection = desiredMovementDirection.normalized;
            //    break;
        }

        ball.DrawDebugRays();
        Debug.DrawRay(ball.transform.position, desiredMovementDirection, Color.gray);
    }

    private void FixedUpdate()
    {
        
        switch (ball.state)
        {
            case PlayerState.Grounded:
                OnFixedUpdateGrounded();
                break;
            case PlayerState.Airborne:
                OnFixedUpdareAirborne();
                break;
            default:
                break;
        }
    }

    void OnFixedUpdateGrounded()
    {
        ball.GroundedMove(desiredMovementDirection);
    }

    void OnFixedUpdareAirborne()
    {
        ball.AirborneMove(desiredMovementDirection);
    }

    public void SwitchGameMode_3D()
    {
        gameMode = GameMode._3D;
    }

    public void SwitchGameMode_2DSide()
    {
        gameMode = GameMode._2DSide;
    }

    //public void SwitchGameMode_2DTopDown()
    //{
    //    gameMode = GameMode._2DTopdown;
    //}
}
