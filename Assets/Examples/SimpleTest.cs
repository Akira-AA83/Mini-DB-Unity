using UnityEngine;
using MiniDB.Unity.SQL;
using System.Threading.Tasks;

/// <summary>
/// Simple test for MiniDB SQL connection
/// </summary>
public class SimpleTest : MonoBehaviour
{
    private MiniDBSQLClient client;
    
    private async void Start()
    {
        Debug.Log("=== MiniDB Simple Test ===");
        
        // Create client
        var clientGO = new GameObject("MiniDBSQLClient");
        client = clientGO.AddComponent<MiniDBSQLClient>();
        
        // Subscribe to events
        client.OnConnected += () => Debug.Log("Connected!");
        client.OnDisconnected += (reason) => Debug.Log($"Disconnected: {reason}");
        client.OnError += (error) => Debug.Log($"Error: {error}");
        
        // Connect and test
        await TestConnection();
    }
    
    private async Task TestConnection()
    {
        Debug.Log("Connecting to MiniDB...");
        
        bool connected = await client.ConnectAsync();
        
        if (connected)
        {
            Debug.Log("Connected successfully!");
            await RunBasicTests();
        }
        else
        {
            Debug.LogError("Failed to connect");
        }
    }
    
    private async Task RunBasicTests()
    {
        Debug.Log("Running basic tests...");
        
        try
        {
            // Test 1: Create a simple session
            Debug.Log("Test 1: Creating game session...");
            string sessionId = await client.CreateGameSession("Test Session", 2);
            Debug.Log($"Session created: {sessionId}");
            
            // Test 2: Send a game event
            Debug.Log("Test 2: Sending game event...");
            await client.SendGameEvent(sessionId, "test_event", new { message = "Hello from Unity!" });
            Debug.Log("Event sent");
            
            // Test 3: Update game state
            Debug.Log("Test 3: Updating game state...");
            await client.UpdateGameState(sessionId, new { status = "running", players = 1 });
            Debug.Log("State updated");
            
            // Test 4: Get active sessions
            Debug.Log("Test 4: Getting active sessions...");
            var sessionsResult = await client.ExecuteQueryAsync("SELECT * FROM game_sessions ORDER BY created_at DESC LIMIT 5");
            Debug.Log($"Sessions query result:");
            Debug.Log(sessionsResult);
            
            // Test 5: Get game events
            Debug.Log("Test 5: Getting game events...");
            var eventsResult = await client.ExecuteQueryAsync("SELECT * FROM game_events ORDER BY timestamp DESC LIMIT 5");
            Debug.Log($"Events query result:");
            Debug.Log(eventsResult);
            
            // Test 6: Show database status
            Debug.Log("Test 6: Checking database status...");
            var tablesResult = await client.ExecuteQueryAsync("SELECT name FROM sqlite_master WHERE type='table'");
            Debug.Log($"Tables in database:");
            Debug.Log(tablesResult);
            
            Debug.Log("All tests completed successfully!");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Test failed: {ex.Message}");
        }
    }
    
    private void OnDestroy()
    {
        if (client != null)
        {
            _ = client.DisconnectAsync();
        }
    }
}