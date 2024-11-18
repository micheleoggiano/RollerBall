using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using CustomInspector;

public enum PlayerState { Grounded, Airborne } // Deve essere accessibile anche al character controller!

public class RollerBall : MonoBehaviour
{
    [CustomInspector.HorizontalLine("References", 5, FixedColor.Gray)]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private GameObject model;
    [SerializeField] private string slamTag = "Slam";
    [SerializeField] private string deadlyTag = "Deadly";
    [SerializeField] private string checkpointTag = "Checkpoint";

    private MovementData activeMovementData;
    private float acceleration, maxSpeed, deceleration, turningFactor;

    public PlayerState state { get; private set; }
    public enum GroundTilt { Flat, Slope, Steep }
    private GroundTilt groundType;
    private float stateTimeStamp;
    private float sphereRadius;
    private Vector3 groundNormal, previousGroundNormal;

    [CustomInspector.HorizontalLine("Movement", 5, FixedColor.Gray)]
    [SerializeField] private MovementData groundMovement;
    [SerializeField,
     Range(0, 1)] private float steepAccelerationMultiplier = .66f;
    [Space]
    [SerializeField] private MovementData airMovement;
    [SerializeField] private bool hasSpeedCappedOnJump = true;
    [SerializeField] private bool canChangeDirectionInAir = true;
    private float takeoffSpeed;
    [Space]
    [SerializeField] private UnityEvent OnBurnout;


    [CustomInspector.HorizontalLine("Slopes", 5, FixedColor.Gray)]
    [Tooltip("The minimum angle at which the terrain is considered a slope. Below this angle, the terrain is treated as flat, allowing unrestricted movement."),
     SerializeField, Range(0f, 90f)]
    private float minSlopeAngle = 0f;
    [Tooltip("The maximum slope angle the player can climb."),
     SerializeField, Range(0f, 90f)]
    private float maxSlopeAngle = 45f;
    [Tooltip("Evaluate if Max Slope Angle value is inclusive or exclusive"),
     SerializeField]
    private bool isAngleValueExclusive = true;
    /*[Tooltip("The maximum angle (inclusive) at which the player can interact with the terrain. Above this angle, the terrain is considered 'steep': the player cannot roll uphill but can still jump."),
     SerializeField, Range(0f, 90f)]*/
    private float maxSteepAngle = 90f;


    [Tooltip("Automatically handles the forces acting on the player during slopes."),
     SerializeField]
    private bool autoSlope = true;
    private float autoSlopeModifier;
    [Tooltip("Manually handles slopes using Movement Data at setted angle range."),
     SerializeField]
    private SlopeHandler[] manualSlopes;
    [System.Serializable]
    public struct SlopeHandler
    {
        //[Range(0f, 90f)] public float lowerAngle; //Non serve
        [Range(0f, 90f)] public float limitAngle;
        public MovementData slopeMovementData;
    }

    [CustomInspector.HorizontalLine("Jumps & Falls", 5, FixedColor.Gray)]
    [SerializeField] private float fallSpeedCap = 12f;
    [SerializeField] private float maxFallTime = 7f;
    [SerializeField, Min(0)] private float coyoteTime =.1f;
    [SerializeField, Min(0)] private float inputBuffer = .1f;
    [SerializeField] public bool precomputedJumpForce = true;
    [SerializeField] private JumpHandler[] jumpDatas;
    [System.Serializable]
    public struct JumpHandler
    {
        public JumpData jumpData;
        public UnityEvent OnJump;
    }
    [SerializeField] private UnityEvent OnLand;

    private bool shouldJump;
    private bool shouldDampenJump;
    private Vector3 cutOffAcceleration;
    private int jumpCount;
    private bool enableJump = true;

    [CustomInspector.HorizontalLine("Dash", 5, FixedColor.Gray)]
    [Tooltip("The cooldown between a completed dash chain and a new one."),
     CustomInspector.ShowIfNot(nameof(rechargeDashOnGround), style = CustomInspector.DisabledStyle.GreyedOut),
     SerializeField] private float dashRechargeTime = .5f; 
    [SerializeField] private bool rechargeDashOnGround;
    [SerializeField] private DashHandler[] dashDatas;
    [System.Serializable]
    public struct DashHandler
    {
        public DashData dashData;
        public UnityEvent OnDash;
    }
    private int dashCount;
    private bool isDashing;

    [CustomInspector.HorizontalLine("Slam", 5, FixedColor.Gray)]
    [SerializeField] private float slamSpeed = 12f;
    [SerializeField] private bool enableSlamBounce = false;
    [CustomInspector.ShowIf(nameof(enableSlamBounce), style = CustomInspector.DisabledStyle.GreyedOut),
     SerializeField] private bool shouldBounceOnDestroyables = false;
    [CustomInspector.ShowIf(nameof(enableSlamBounce), style = CustomInspector.DisabledStyle.GreyedOut),
     SerializeField] private bool canSlamAfterBounce = false;
    [CustomInspector.ShowIf(nameof(enableSlamBounce), style = CustomInspector.DisabledStyle.GreyedOut),
     SerializeField] private bool allowDoubleJump = false;
    [CustomInspector.ShowIf(nameof(enableSlamBounce), style = CustomInspector.DisabledStyle.GreyedOut),
     SerializeField] private JumpData overrideJumpDataOnBounce;
    public UnityEvent OnSlam;

    private bool isSlamming;
    private bool isEnteringSlamBounce;

    [CustomInspector.HorizontalLine("Debug", 3, FixedColor.Red)]
    public TextMeshProUGUI debugPanel;
    bool isDebug;

    #region SETUP
    void OnValidate()
    {
        SetupSlopes();
    }
    private void Start()
    {
        rb.drag = rb.angularDrag = 0f;
        sphereRadius = GetComponent<SphereCollider>().radius;
        SetupMovement();

        var gravity = Physics.gravity;

        for (int i = 0; i < jumpDatas.Length; i++)
        {
            var jump = jumpDatas[i];
        }

        state = PlayerState.Airborne;
    }
    private void SetupMovement()
    {
        //groundNormal = Vector3.up;
        //SwitchMovementData(groundMovement);

        groundNormal = Vector3.zero;
        SwitchMovementData(airMovement);
    }
    private void SetupSlopes()
    {
        minSlopeAngle = Mathf.Min(minSlopeAngle, maxSlopeAngle);
        maxSteepAngle = Mathf.Max(maxSteepAngle, maxSlopeAngle);

        for (int i = 0; i < manualSlopes.Length; i++)
        {
            var slope = manualSlopes[i];
            var limitAngle = slope.limitAngle;
            var lowerAngle = (i - 1) < 0 ? minSlopeAngle : manualSlopes[i - 1].limitAngle;

            limitAngle = Mathf.Max(lowerAngle + .1f, limitAngle);
            limitAngle = Mathf.Min(limitAngle, maxSlopeAngle);

            manualSlopes[i].limitAngle = limitAngle;
        }

        manualSlopes[manualSlopes.Length - 1].limitAngle = maxSlopeAngle;
    }
    private void Update() //debug
    {
        if (debugPanel == null) return;
        debugPanel.text = "State: " + state +
                            "<br>Ground Type: " + /*activeMovementData*/ groundType +
                            "<br>Movement Speed: " + Mathf.RoundToInt(Vector3.ProjectOnPlane(rb.velocity, GetGroundNormal()).magnitude * 100f) / 100f /*+
                            "<br>Character Speed: " + Mathf.RoundToInt(rb.velocity.magnitude * 100f) / 100f*/;
    }
    #endregion
    #region STATE MACHINE
    public void SwitchState(PlayerState newState)
    {
        if (state == newState) return; 
        OnExitState(state);
        state = newState;
        OnEnterState(newState);
    }

    void OnEnterState(PlayerState state)
    {
        stateTimeStamp = Time.time;
        switch (state)
        {
            case PlayerState.Grounded:                
                SetGroundedMovementData();
                jumpCount = 0;
                break;
            case PlayerState.Airborne:
                SwitchMovementData(airMovement);
                groundNormal = -Physics.gravity.normalized;
                break;
            default:
                break;
        }
    }
    void OnExitState(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.Grounded:
                break;
            case PlayerState.Airborne:
                OnLand.Invoke();
                break;
            default:
                break;
        }
    }
    #endregion
    #region COLLISIONS
    private void OnCollisionEnter(Collision collision)
    {
        CheckCollision(collision);
    }

    void OnCollisionStay(Collision collision)
    {
        previousGroundNormal = groundNormal; // Workaround bug unity, per evitare errori durante i salti a contatto con pareti vericali

        switch (state)
        {
            case PlayerState.Grounded:
                SetGroundNormal(collision.contacts);
                SetGroundedMovementData();
                break;
            case PlayerState.Airborne:
                SetGroundNormal(collision.contacts);
                if (GetGroundNormal() != Vector3.zero) SwitchState(PlayerState.Grounded);
                break;
            default:
                break;
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision.contactCount == 0)
        {
            groundNormal = Vector3.zero;
            jumpCount = 1; //Da verificare
            SwitchState(PlayerState.Airborne);
        }
    }

    private void CheckCollision(Collision collision)
    {
        if (this.gameObject.tag == slamTag)
        {
            isEnteringSlamBounce = true;
        }
        
        if (collision.gameObject.tag == deadlyTag)
        {
            GameManager.instance.ReloadCheckpoint();
            rb.velocity = Vector3.zero;
        }

        if (collision.gameObject.tag == checkpointTag)
        {
            GameManager.instance.SaveCheckpoint(collision.transform);
        }
    }
    #endregion
    #region NORMAL GETTER & SETTER
    void SetGroundNormal(ContactPoint[] contacts)
    {        
        var gravity = Physics.gravity;
        //float terrainAngle = 90f; // Devo pensare anche ai casi in cui la collisione è col soffitto!
        //float terrainAngle = 180f; // Devo pensare anche ai casi in cui la collisione è col soffitto!
        float terrainAngle = maxSteepAngle; // Devo pensare anche ai casi in cui la collisione è col soffitto!

        groundNormal = Vector3.zero;

        foreach (var contact in contacts)
        {
            float angle = Vector3.Angle(-gravity, contact.normal);

            if (angle <= terrainAngle)
            {
                terrainAngle = angle;
                groundNormal = contact.normal;
            }
        }
    }
    Vector3 GetGroundNormal()
    {
        // Workaround OnCollisionEnter\Stay bug
        var gravityNormal = -Physics.gravity.normalized;
        var groundNormalDot = Vector3.Dot(groundNormal, gravityNormal);
        var previousGroundNormalDot = Vector3.Dot(previousGroundNormal, gravityNormal);

        if (groundNormalDot >= previousGroundNormalDot)
        {
            return groundNormal;
        }
        else
        {
            return previousGroundNormal;
        }
    }
    #endregion
    #region GROUND TILT EVALUATION FUNCTIONS
    void SwitchGroundTilt(GroundTilt newGroundType)
    {
        groundType = newGroundType; // Funzioni di enter?
        jumpCount = 0;

        switch (newGroundType)
        {
            case GroundTilt.Flat:
            case GroundTilt.Steep: // Valutare se mettere una movemente data per steep
                SwitchMovementData(groundMovement);
                break;
            case GroundTilt.Slope:
                if (autoSlope) SwitchMovementData(groundMovement);
                else EvaluateSlopeMovementData();
                break;
        }
    }
    void SetGroundedMovementData()
    {
        if (GetGroundNormal() != Vector3.zero) // Should always be
        {
            var terrainAngle = Vector3.Angle(GetGroundNormal(), -Physics.gravity);
            bool isNotSlope = isAngleValueExclusive ? terrainAngle < minSlopeAngle : terrainAngle <= minSlopeAngle;

            if (isNotSlope)
            {
                SwitchGroundTilt(GroundTilt.Flat);
            }
            else
            {
                bool isSlope = isAngleValueExclusive ? terrainAngle < maxSlopeAngle : terrainAngle <= maxSlopeAngle;
                if (terrainAngle > minSlopeAngle && isSlope)
                {
                    SwitchGroundTilt(GroundTilt.Slope);
                }
                else if (terrainAngle <= maxSteepAngle)
                {
                    SwitchGroundTilt(GroundTilt.Steep);
                }
                else // terrain > 90
                {
                    groundNormal = Vector3.zero;
                }
            }
        }
        else
        {
            SwitchState(PlayerState.Airborne);
        }
    }

    void EvaluateSlopeMovementData()
    {
        var angle = Vector3.Angle(Physics.gravity, -groundNormal);

        for (int i = 0; i < manualSlopes.Length; i++)
        {
            var slope = manualSlopes[i];
            bool isInsideLimit = isAngleValueExclusive ? angle < slope.limitAngle : angle <= slope.limitAngle;

            if (isInsideLimit)
            {
                SwitchMovementData(slope.slopeMovementData);
                return;
            }
        }
    }
    float AutoSlopeModifier(Vector3 normal)
    {
        var angle = Vector3.Angle(Physics.gravity, -normal);
        //var value = Mathf.Clamp01((angle - minSlopeAngle) / (maxSlopeAngle - minSlopeAngle));
        var value = Mathf.Clamp((angle - minSlopeAngle) / (maxSlopeAngle - minSlopeAngle), 0, steepAccelerationMultiplier);
        return 1f - value;
    }
    #endregion
    #region GROUNDED & AIRBORN MOVEMENT
    #region GROUNDED FUNCTIONS
    public void GroundedMove(Vector3 desiredDirection)
    {
        if (shouldJump) { ApplyJump(); return; }

        var movementPlaneNormal = GetGroundNormal() != Vector3.zero ? GetGroundNormal() : -Physics.gravity.normalized; // Teoricamente, non è mai zero.

        desiredDirection = RotateVector(desiredDirection, movementPlaneNormal);

        var normalPlaneVelocity = Vector3.ProjectOnPlane(rb.velocity, movementPlaneNormal); // OnEnter: Ruota la normalPlaneVelocity?
        var normalAlignedVelocity = Vector3.Project(rb.velocity, movementPlaneNormal);

        if (autoSlope) autoSlopeModifier = AutoSlopeModifier(movementPlaneNormal);
        else autoSlopeModifier = 1f;

        // Set sulla base della Movement Data attiva
        maxSpeed = activeMovementData.maxSpeed;
        acceleration = activeMovementData.acceleration;
        deceleration = activeMovementData.deceleration;
        turningFactor = activeMovementData.turningFactor;

        switch (groundType)
        {
            case GroundTilt.Flat:
                UpdateVelocityOnFlatGround(ref normalPlaneVelocity, desiredDirection, movementPlaneNormal);   
                break;
            case GroundTilt.Slope:
                UpdateVelocityOnSlopeGround(ref normalPlaneVelocity, desiredDirection, movementPlaneNormal);
                break;
            case GroundTilt.Steep:
                UpdateVelocityOnSteepGround(ref normalPlaneVelocity, desiredDirection, movementPlaneNormal);
                break;
        }
        Debug.DrawRay(transform.position, desiredDirection, Color.black);
        ModelRotation(normalPlaneVelocity);
        rb.velocity = normalPlaneVelocity + normalAlignedVelocity;
    }
    private void UpdateVelocityOnFlatGround(ref Vector3 normalPlaneVelocity, Vector3 desiredDirection, Vector3 movementPlaneNormal)
    {
        if (desiredDirection * autoSlopeModifier != Vector3.zero)
        {
            if (Vector3.Dot(desiredDirection, normalPlaneVelocity) >= 0f) turningFactor = 1f;
            else OnBurnout.Invoke();

            maxSpeed *= autoSlopeModifier;
            acceleration *= autoSlopeModifier * turningFactor;
            normalPlaneVelocity = Vector3.MoveTowards(normalPlaneVelocity, desiredDirection * maxSpeed, acceleration * Time.fixedDeltaTime);
        }
        else
        {
            normalPlaneVelocity = Vector3.MoveTowards(normalPlaneVelocity, Vector3.zero, deceleration * Time.fixedDeltaTime);
        }

        rb.AddForce(-1f * GravityProjectionOnPlane(movementPlaneNormal), ForceMode.Acceleration);
    }

    private void UpdateVelocityOnSlopeGround(ref Vector3 normalPlaneVelocity, Vector3 desiredDirection, Vector3 movementPlaneNormal)
    {
        var directionDotClimb = Vector3.Dot(desiredDirection, -1 * GravityProjectionOnPlane(movementPlaneNormal).normalized); // is climbing?

        if (desiredDirection * autoSlopeModifier != Vector3.zero && directionDotClimb >= 0)
        {
            // Modificatori
            if (Vector3.Dot(desiredDirection, normalPlaneVelocity) >= 0f) turningFactor = 1f;
            else OnBurnout.Invoke();

            maxSpeed *= autoSlopeModifier;
            acceleration = Mathf.Lerp(groundMovement.acceleration, acceleration, directionDotClimb);
            acceleration *= autoSlopeModifier * turningFactor;
            normalPlaneVelocity = Vector3.MoveTowards(normalPlaneVelocity, desiredDirection * maxSpeed, acceleration * Time.fixedDeltaTime);
            rb.AddForce(-1f * GravityProjectionOnPlane(movementPlaneNormal), ForceMode.Acceleration);
        }
        else
        {
            normalPlaneVelocity = Vector3.MoveTowards(normalPlaneVelocity, Vector3.zero, deceleration * Time.fixedDeltaTime);
            normalPlaneVelocity = Vector3.ClampMagnitude(normalPlaneVelocity, fallSpeedCap);
        }
    }

    private void UpdateVelocityOnSteepGround(ref Vector3 normalPlaneVelocity, Vector3 desiredDirection, Vector3 movementPlaneNormal)
    {
        if (desiredDirection != Vector3.zero)
        {
            acceleration *= steepAccelerationMultiplier;
            acceleration = Mathf.Clamp(acceleration, 0, Physics.gravity.magnitude);
            normalPlaneVelocity = Vector3.MoveTowards(normalPlaneVelocity, desiredDirection * maxSpeed, acceleration * Time.fixedDeltaTime);
            OnBurnout.Invoke();
        }
        else
        {
            normalPlaneVelocity = Vector3.MoveTowards(normalPlaneVelocity, Vector3.zero, deceleration * Time.fixedDeltaTime);
            normalPlaneVelocity = Vector3.ClampMagnitude(normalPlaneVelocity, fallSpeedCap);
        }
    }
    #endregion
    #region AIRBORNE FUNCTIONS
    public void AirborneMove(Vector3 desiredDirection)
    {
        if (Time.time - stateTimeStamp >= maxFallTime)
        {
            GameManager.instance.ReloadCheckpoint();
            rb.velocity = Vector3.zero;
            stateTimeStamp = Time.time;
        }

        if (shouldJump) { ApplyJump(); return; }

        var movementNormal = -Physics.gravity.normalized;

        maxSpeed = activeMovementData.maxSpeed;
        acceleration = activeMovementData.acceleration;
        deceleration = activeMovementData.deceleration;
        turningFactor = activeMovementData.turningFactor;

        var normalPlaneVelocity = Vector3.ProjectOnPlane(rb.velocity, movementNormal);
        var normalAlignedVelocity = Vector3.Project(rb.velocity, movementNormal);

        HandleGravityModifiersOnJump(movementNormal, normalAlignedVelocity);

        UpdateAerialVelocity(ref normalPlaneVelocity, desiredDirection);

        ModelRotation(normalPlaneVelocity);

        rb.velocity = normalPlaneVelocity + normalAlignedVelocity;

        CapToTermninalFallVelocity();
    }

    private void UpdateAerialVelocity(ref Vector3 normalPlaneVelocity, Vector3 desiredDirection)
    {
        if (desiredDirection != Vector3.zero)
        {
            turningFactor = Vector3.Dot(desiredDirection, normalPlaneVelocity) >= 0f ? 1f : turningFactor;
            acceleration *= turningFactor;
            if (hasSpeedCappedOnJump) maxSpeed = takeoffSpeed;
            acceleration = normalPlaneVelocity.sqrMagnitude <= Mathf.Pow(maxSpeed, 2) ? acceleration : deceleration;

            var targetSpeed = canChangeDirectionInAir ? desiredDirection * maxSpeed : Vector3.zero;

            normalPlaneVelocity = Vector3.MoveTowards(normalPlaneVelocity, targetSpeed, acceleration * Time.fixedDeltaTime);
        }
        else
        {
            normalPlaneVelocity = Vector3.MoveTowards(normalPlaneVelocity, Vector3.zero, deceleration * Time.fixedDeltaTime);
        }        
    }
    #endregion
    void SwitchMovementData(MovementData newMovementData)
    {
        activeMovementData = newMovementData;
    }
    private Vector3 RotateVector(Vector3 vectorToRotate, Vector3 planeNormal)
    {
        var directionNormal = -Physics.gravity.normalized;
        Vector3 rotationAxis = Vector3.Cross(directionNormal, planeNormal);
        float angle = Mathf.Acos(Vector3.Dot(directionNormal, planeNormal)) * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.AngleAxis(angle, rotationAxis);

        var rotatedVector = rotation * vectorToRotate;

        return rotatedVector;
    }
    private Vector3 GravityProjectionOnPlane(Vector3 planeNormal)
    {
        return Vector3.ProjectOnPlane(Physics.gravity, planeNormal);
    }    
    #endregion
    #region SPHERE ROTATION
    public void ModelRotation(Vector3 velocity)
    {
        Vector3 rotationAxis = Vector3.Cross(Vector3.up, velocity);

        float angularVelocity = velocity.magnitude / sphereRadius * Mathf.Rad2Deg;
        float angle = angularVelocity * Time.fixedDeltaTime;

        model.transform.Rotate(rotationAxis, angle, Space.World);
    }
    #endregion
    #region EVENTS
    #endregion
    #region JUMP & FALL
    public IEnumerator TryJump()
    {
        if (!enableJump) yield break;

        switch (state)
        {
            case PlayerState.Grounded:
                if (jumpCount >= jumpDatas.Length) yield break;
                shouldJump = true;
                shouldDampenJump = false;
                break;
            case PlayerState.Airborne:
                if (jumpCount == 0 && Time.time - stateTimeStamp <= coyoteTime)
                {
                    shouldJump = true;
                    shouldDampenJump = false;
                }
                else if (jumpCount > 0 && jumpCount < jumpDatas.Length)
                {
                    shouldJump = true;
                    shouldDampenJump = false;
                }
                else if (jumpCount >= jumpDatas.Length)
                {
                    var waitFrames = Mathf.RoundToInt(inputBuffer / Time.fixedDeltaTime);
                    for (int i = 0; i < waitFrames; i++)
                    {
                        if (state == PlayerState.Grounded)
                        {
                            shouldJump = true;
                            shouldDampenJump = false;
                            yield break;
                        }
                        else
                        {
                            yield return new WaitForEndOfFrame();
                        }
                    }
                }
                break;
            default:
                break;
        }
    }

    //public void TryJump()
    //{
    //    if (jumpCount < jumpDatas.Length)
    //    {
    //        shouldJump = true;
    //        shouldDampenJump = false;
    //    }
    //}

    void ApplyJump()
    {
        shouldJump = false;

        var gravity = Physics.gravity;
        var jumpHeight = jumpDatas[jumpCount].jumpData.jumpHeight;
        var gravityDampOnRise = jumpDatas[jumpCount].jumpData.gravityDampOnRise;
        var jumpEvent = jumpDatas[jumpCount].OnJump;

        var jumpNormal = GetGroundNormal() != Vector3.zero ? GetGroundNormal() : -gravity.normalized; // Security check

        Debug.DrawRay(transform.position, GetGroundNormal(), Color.white, 3f);

        //previousGroundNormal = Vector3.zero;
        var gravityMagnitude = precomputedJumpForce ? GameManager.instance.GetInitialGravity() : gravity.magnitude;
        var jumpVelocity = Mathf.Sqrt(2 * jumpHeight * gravityMagnitude * gravityDampOnRise);

        var velocity = rb.velocity;
        var angle = Vector3.Angle(gravity, -jumpNormal);
        var normalPlaneVelocity = angle >= minSlopeAngle ? Vector3.ProjectOnPlane(velocity, jumpNormal) : Vector3.zero;
        var normalAlignedVelocity = Vector3.Project(velocity, jumpNormal);
        rb.velocity += jumpVelocity * jumpNormal - normalPlaneVelocity - normalAlignedVelocity;

        takeoffSpeed = Mathf.Clamp(Vector3.ProjectOnPlane(velocity, gravity).magnitude, 0, airMovement.maxSpeed);

        jumpEvent.Invoke();
        jumpCount++;
    }

    void ApplyJump(JumpData jumpDataOverride, int jumpCountOverride)
    {
        shouldJump = false;

        var gravity = Physics.gravity;
        var jumpHeight = jumpDataOverride.jumpHeight;
        var gravityDampOnRise = jumpDataOverride.gravityDampOnRise;
        //var jumpEvent = jumpDatas[jumpCount].OnJump;

        var jumpNormal = GetGroundNormal() != Vector3.zero ? GetGroundNormal() : -gravity.normalized; // Security check

        Debug.DrawRay(transform.position, GetGroundNormal(), Color.white, 3f);

        //previousGroundNormal = Vector3.zero;

        var gravityMagnitude = precomputedJumpForce ? GameManager.instance.GetInitialGravity() : gravity.magnitude;
        var jumpVelocity = Mathf.Sqrt(2 * jumpHeight * gravityMagnitude * gravityDampOnRise);

        var velocity = rb.velocity;
        var angle = Vector3.Angle(gravity, -jumpNormal);
        var normalPlaneVelocity = angle >= minSlopeAngle ? Vector3.ProjectOnPlane(velocity, jumpNormal) : Vector3.zero;
        var normalAlignedVelocity = Vector3.Project(velocity, jumpNormal);
        rb.velocity += jumpVelocity * jumpNormal - normalPlaneVelocity - normalAlignedVelocity;

        takeoffSpeed = Mathf.Clamp(Vector3.ProjectOnPlane(velocity, gravity).magnitude, 0, airMovement.maxSpeed);

        //jumpEvent.Invoke();
        jumpCount = jumpCountOverride;
    }

    public void ApplyBounce()
    {
        StartCoroutine(BounceRoutine());
    }

    IEnumerator BounceRoutine()
    {
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        Bounce();
    }

    void Bounce()
    {
        shouldJump = false;

        var gravity = Physics.gravity;
        var jumpHeight = jumpDatas[0].jumpData.jumpHeight;
        var gravityDampOnRise = jumpDatas[0].jumpData.gravityDampOnRise;
        var jumpEvent = jumpDatas[0].OnJump;

        var jumpNormal = GetGroundNormal() != Vector3.zero ? GetGroundNormal() : -gravity.normalized; // Security check

        Debug.DrawRay(transform.position, GetGroundNormal(), Color.white, 3f);

        //previousGroundNormal = Vector3.zero;
        var gravityMagnitude = precomputedJumpForce ? GameManager.instance.GetInitialGravity() : gravity.magnitude;
        var jumpVelocity = Mathf.Sqrt(2 * jumpHeight * gravityMagnitude * gravityDampOnRise);

        var velocity = rb.velocity;
        var angle = Vector3.Angle(gravity, -jumpNormal);
        var normalPlaneVelocity = angle >= minSlopeAngle ? Vector3.ProjectOnPlane(velocity, jumpNormal) : Vector3.zero;
        var normalAlignedVelocity = Vector3.Project(velocity, jumpNormal);
        rb.velocity += jumpVelocity * jumpNormal - normalPlaneVelocity - normalAlignedVelocity;

        takeoffSpeed = Mathf.Clamp(Vector3.ProjectOnPlane(velocity, gravity).magnitude, 0, airMovement.maxSpeed);

        jumpEvent.Invoke();
        jumpCount = 1;
    }

    public void EvaluateJumpCutOff()
    {
        var gravity = Physics.gravity;
        var upwardVelocity = Vector3.Dot(rb.velocity, gravity);
        var normalAlignedVelocity = Vector3.Project(rb.velocity, gravity.normalized);

        if (upwardVelocity < 0f)
        {
            shouldDampenJump = true;

            var cutoffCount = Math.Clamp(jumpCount, 1, jumpDatas.Length);
            var jumpCutOffFactor = jumpDatas[cutoffCount - 1].jumpData.jumpCutOffFactor;
            var newGravity = gravity * jumpCutOffFactor;

            newGravity = Vector3.ClampMagnitude(newGravity, normalAlignedVelocity.magnitude / Time.fixedDeltaTime);
            cutOffAcceleration = newGravity - gravity;
            cutOffAcceleration = Vector2.Dot(cutOffAcceleration, gravity) >= 0f ? cutOffAcceleration : Vector3.zero; // Serve come sicurezza per gli errori di virgola mobile
        }
    }
    private void HandleGravityModifiersOnJump(Vector3 movementNormal, Vector3 normalAlignedVelocity)
    {
        if (Vector3.Dot(normalAlignedVelocity, movementNormal) > 0)
        {
            if (shouldDampenJump)
            {
                rb.AddForce(cutOffAcceleration, ForceMode.Acceleration); //Apply cut off
            }
            else if (jumpCount > 0)
            {
                var gravityDampOnRise = jumpDatas[jumpCount - 1].jumpData.gravityDampOnRise;
                Vector3 gravity = Physics.gravity;
                gravity = precomputedJumpForce ? GameManager.instance.GetInitialGravity() * gravity.normalized : gravity;

                //rb.AddForce(-Physics.gravity * (1f - gravityDampOnRise), ForceMode.Acceleration); // Ci vorrebbe un sistema di controllo per vedere a quali ascensioni applicarlo e a quali no?
                rb.AddForce(-gravity * (1f - gravityDampOnRise), ForceMode.Acceleration); // Ci vorrebbe un sistema di controllo per vedere a quali ascensioni applicarlo e a quali no?
            }
        }
        else
        {
            shouldDampenJump = false;
        }
    }
    void CapToTermninalFallVelocity()
    {
        var velocity = rb.velocity;

        if (Vector2.Dot(velocity, Physics.gravity) <= 0) return;

        Vector3.ClampMagnitude(velocity, fallSpeedCap);

        rb.velocity = velocity;
    }
    #endregion
    #region DASH
    public IEnumerator TryDash(Vector3 desiredDirection, Vector3 camForward) // E se lasciassi la scelta?
    {
        if (dashCount >= dashDatas.Length || isDashing) yield break;

        isDashing = true;

        var data = dashDatas[dashCount].dashData;
        var dashSpeed = data.dashSpeed;
        var dashDuration = data.dashDuration;
        var canChangeDirection = data.canChangeDirection;
        var dashFreezeFrames = data.dashFreezeFrames;
        var dashEvent = dashDatas[dashCount].OnDash;

        dashCount++;

        Vector3 dashDirection;
        if (canChangeDirection) // Scelta per ogni data?
        {
            dashDirection = desiredDirection != Vector3.zero ? desiredDirection : camForward;
        }
        else
        {
            var movementPlaneVelocity = Vector3.ProjectOnPlane(rb.velocity, -Physics.gravity.normalized);
            dashDirection = movementPlaneVelocity != Vector3.zero ? movementPlaneVelocity.normalized : camForward;
        }

        var activeDashFrames = Mathf.RoundToInt(dashDuration / Time.fixedDeltaTime);
        var gravity = Physics.gravity;

        //PlayOnDashEvent();
        dashEvent.Invoke();

        for (int i = 0; i < dashFreezeFrames; i++)
        {
            rb.velocity = Vector3.zero;
            rb.AddForce(-gravity, ForceMode.Acceleration);
            ModelRotation(rb.velocity);
            yield return new WaitForFixedUpdate();
        }

        for (int i = 0; i < activeDashFrames; i++)
        {
            rb.velocity = dashDirection * dashSpeed;
            ModelRotation(rb.velocity);
            rb.AddForce(-gravity, ForceMode.Acceleration);
            yield return new WaitForFixedUpdate();
        }

        isDashing = false;

        if (dashCount < dashDatas.Length) yield break;

        //dashTimeStamp = Time.time;

        if (rechargeDashOnGround)
        {
            while (state != PlayerState.Grounded) yield return new WaitForFixedUpdate();
            dashCount = 0;
        }
        else
        {
            var dashCooldownFrames = Mathf.RoundToInt(dashRechargeTime / Time.fixedDeltaTime);
            for (int i = 0; i < dashCooldownFrames; i++) yield return new WaitForFixedUpdate();
            dashCount = 0;
        }
    }
    #endregion
    #region SLAM
    public IEnumerator Slam()
    {
        if (isSlamming) yield break;
        isSlamming = true;

        var originalTag = this.gameObject.tag;
        this.gameObject.tag = slamTag;
        bool slamCondition = shouldBounceOnDestroyables ? state == PlayerState.Airborne && !isEnteringSlamBounce : state == PlayerState.Airborne;

        while (slamCondition)
        {
            rb.velocity = Physics.gravity.normalized * fallSpeedCap;
            yield return new WaitForFixedUpdate();
            slamCondition = shouldBounceOnDestroyables ? state == PlayerState.Airborne && !isEnteringSlamBounce : state == PlayerState.Airborne;
        }
        
        isEnteringSlamBounce = false;
        this.gameObject.tag = originalTag;

        yield return new WaitForFixedUpdate();

        OnSlam.Invoke();

        if (enableSlamBounce)
        {
            var originalJumpData = jumpDatas[0].jumpData;
            //var jumpCountOverride = allowDoubleJump ? 1 : jumpDatas.Length;
            if (!allowDoubleJump) enableJump = false;
            jumpDatas[0].jumpData = overrideJumpDataOnBounce;

            shouldJump = true;
            shouldDampenJump = false;
            ApplyJump(overrideJumpDataOnBounce, 1);

            yield return new WaitForFixedUpdate(); //esecuzione
            yield return new WaitForFixedUpdate(); //distaccamento da terra

            if (canSlamAfterBounce) isSlamming = false;

            while (state != PlayerState.Grounded)
            {
                yield return new WaitForFixedUpdate();
            }

            jumpDatas[0].jumpData = originalJumpData;
            enableJump = true;
        }

        isSlamming = false;        
    }
    #endregion

    #region ANIMATIONS

    #endregion
    #region RENDER



    #endregion
    #region MISCELLANEOUS
    #endregion

    public void DrawDebugRays()
    {
        // Normal
        Debug.DrawRay(transform.position, GetGroundNormal(), Color.green);
        // Velocity
        Debug.DrawRay(transform.position, rb.velocity, Color.blue);
    }

    public float GetMinSlopeAngle()
    {
        return minSlopeAngle;
    }

    public void SetVelocity(Vector3 velocity)
    {
        rb.velocity = velocity;
    }
}

