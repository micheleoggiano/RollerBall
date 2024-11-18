using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider))]
public class CollisionBehaviour : MonoBehaviour
{
    [SerializeField] private string objectTag = "Player";
    [SerializeField] private UnityEvent OnEnter;
    [SerializeField] private UnityEvent OnStay;
    [SerializeField] private UnityEvent OnExit;

    private void OnCollisionEnter(Collision collision)
    {
        if (string.Equals(collision.gameObject.tag, objectTag)) OnEnter.Invoke();
    }

    private void OnCollisionStay(Collision collision)
    {
        if (string.Equals(collision.gameObject.tag, objectTag)) OnStay.Invoke();
    }

    private void OnCollisionExit(Collision collision)
    {
        if (string.Equals(collision.gameObject.tag, objectTag)) OnExit.Invoke();
    }
}
