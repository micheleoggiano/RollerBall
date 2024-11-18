using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEditor.SceneManagement;
using CustomInspector;
using Cinemachine;

public class GameManager : MonoBehaviour
{
    public static GameManager instance;
    #region 🡹WTF IS THAT? THE SINGLETON PATTERN
    /* This step is a bit technical. You don't need to understand the code, only the concept.
     * The "singleton pattern" is a design pattern that ensures that only one instance of a certain class is created,
     * and provides a global access point to this instance.
     * You can indeed access the public variables or public methods of the game manager
     * by writing GameManager.instance, followed by the name of the public variable or method.
     * The game requires a single game manager, and no more than one can exist.
     * Unity, by default, assigns a cog icon when a script named GameManager is created.
     */
    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            //DontDestroyOnLoad(this.gameObject);
        }
        else if (instance != this)
        {
            Destroy(this.gameObject);
        }
    }
    #endregion

    private Vector3 checkpoint;
    private GameObject player;
    private CinemachineBrain cinemachineBrain;

    [CustomInspector.HorizontalLine("Gravity", 5, FixedColor.Red)]
    [SerializeField] private Transform upTransform;
    [SerializeField] private float gravityMagnitude = 10f;
    [SerializeField] private float gravityTransitionDuration = 1f;
    [Button(nameof(ChangeGravity), label = "Change Gravity")]
    [CustomInspector.HorizontalLine("Events", 5, FixedColor.Gray)]
    [SerializeField] private UnityEvent OnReloadCheckpoint;
    [SerializeField] private UnityEvent OnGameOver;

    private Transform finalWorldUpTransform;
    private float initialGravity;


    private void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player");
        checkpoint = player.transform.position;

        cinemachineBrain = Camera.main.GetComponent<CinemachineBrain>();
        if (upTransform == null)
        {
            GameObject worldUpObject = new GameObject("UpTransform");
            worldUpObject.transform.rotation = Quaternion.identity;
            upTransform = worldUpObject.transform;
            finalWorldUpTransform = upTransform;
            cinemachineBrain.m_WorldUpOverride = upTransform;
        }
        else
        {
            cinemachineBrain.m_WorldUpOverride = upTransform;
        }

        Physics.gravity = upTransform.up * -gravityMagnitude;
        initialGravity = gravityMagnitude;
    }

    private void ChangeGravity()
    {
        Physics.gravity = upTransform.up * -gravityMagnitude;
    }

    public void ChangeGravityDirection(Transform transformReference)
    {
        finalWorldUpTransform = transformReference;
        StartCoroutine(TransitionNewUp());
    }

    public void ChangeGravityMagnitude(float magnitude)
    {
        gravityMagnitude = magnitude;
        Physics.gravity = finalWorldUpTransform.up * -gravityMagnitude;
    }

    IEnumerator TransitionNewUp()
    {
        Quaternion startRotation = upTransform.rotation;
        Quaternion endRotation = finalWorldUpTransform.rotation;

        Physics.gravity = finalWorldUpTransform.up * -gravityMagnitude;

        float elapsedTime = 0f;

        while (elapsedTime < gravityTransitionDuration)
        {
            elapsedTime += Time.deltaTime;

            upTransform.rotation = Quaternion.Lerp(startRotation, endRotation, elapsedTime / gravityTransitionDuration);
            cinemachineBrain.m_WorldUpOverride = upTransform;

            yield return null;
        }

        upTransform.rotation = endRotation;
        cinemachineBrain.m_WorldUpOverride = upTransform;
    }

    public float GetInitialGravity()
    {
        return initialGravity;
    }


    public void SaveCheckpoint(Transform newTransform)
    {
        checkpoint = newTransform.position;
    }

    public void ReloadCheckpoint()
    {
        player.transform.position = checkpoint;
    }

    public void RestartScene()
    {
        EditorSceneManager.LoadScene(0);
    }
}
