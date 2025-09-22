using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple dispatcher for executing actions on Unity's main thread
/// </summary>
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    private static readonly object _lock = new object();
    
    public static UnityMainThreadDispatcher Instance
    {
        get
        {
            if (_instance == null)
            {
                // Only search if we're on the main thread
                if (UnityEngine.Object.FindObjectOfType<UnityMainThreadDispatcher>() != null)
                {
                    _instance = UnityEngine.Object.FindObjectOfType<UnityMainThreadDispatcher>();
                }
                else
                {
                    // Create new instance on main thread only
                    GameObject go = new GameObject("UnityMainThreadDispatcher");
                    _instance = go.AddComponent<UnityMainThreadDispatcher>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }
    
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }
    
    private void Update()
    {
        lock (_lock)
        {
            while (_executionQueue.Count > 0)
            {
                try
                {
                    _executionQueue.Dequeue().Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Main thread dispatcher execution error: {ex.Message}");
                }
            }
        }
    }
    
    public void Enqueue(Action action)
    {
        if (action == null) return;
        
        lock (_lock)
        {
            _executionQueue.Enqueue(action);
        }
    }
}