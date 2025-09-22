using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

namespace MiniDB.Unity.SQL
{
    /// <summary>
    /// Simple SQL client for MiniDB
    /// </summary>
    public class MiniDBSQLClient : MonoBehaviour
    {
        [Header("Connection Settings")]
        [SerializeField] private string serverAddress = "localhost";
        [SerializeField] private int serverPort = 8080;
        [SerializeField] private string databaseName = "gaming";
        [SerializeField] private bool enableDebugLogs = true;
        
        private ClientWebSocket webSocket;
        private CancellationTokenSource cancellationTokenSource;
        private string playerId;
        private string playerName;
        private Task receiveTask;
        private TaskCompletionSource<string> latestResponse;
        private readonly SemaphoreSlim querySemaphore = new SemaphoreSlim(1, 1);
        
        public bool IsConnected => webSocket?.State == WebSocketState.Open;
        public string PlayerId => playerId;
        public string PlayerName => playerName;
        
        // Events
        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<string> OnError;
        public event Action<string> OnTableNotification;
        
        private void Awake()
        {
            // Generate TRULY unique player ID for each client instance
            // This ensures Unity Editor and Build have different IDs
            playerId = System.Guid.NewGuid().ToString();
            playerName = "Player_" + UnityEngine.Random.Range(1000, 9999);
            
            if (enableDebugLogs)
                Debug.Log($"[MiniDBSQL] Generated unique player: {playerName} (ID: {playerId})");
        }
        
        public async Task<bool> ConnectAsync()
        {
            try
            {
                if (webSocket != null)
                {
                    webSocket.Dispose();
                }
                
                webSocket = new ClientWebSocket();
                cancellationTokenSource = new CancellationTokenSource();
                
                string url = "ws://" + serverAddress + ":" + serverPort;
                Uri uri = new Uri(url);
                
                if (enableDebugLogs)
                    Debug.Log("[MiniDBSQL] Connecting to " + url + "...");
                
                await webSocket.ConnectAsync(uri, cancellationTokenSource.Token);
                
                if (webSocket.State == WebSocketState.Open)
                {
                    // Start receiving messages
                    receiveTask = StartReceiving();
                    
                    OnConnected?.Invoke();
                    
                    // Initialize database and tables
                    await InitializeDatabase();
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError("[MiniDBSQL] Connection error: " + ex.Message);
                OnError?.Invoke(ex.Message);
                return false;
            }
        }
        
        public async Task DisconnectAsync()
        {
            try
            {
                cancellationTokenSource?.Cancel();
                
                if (webSocket?.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
                }
                
                webSocket?.Dispose();
                cancellationTokenSource?.Dispose();
                
                // Cancel pending response
                if (latestResponse != null)
                {
                    latestResponse.TrySetCanceled();
                    latestResponse = null;
                }
                
                OnDisconnected?.Invoke("Manual disconnect");
            }
            catch (Exception ex)
            {
                Debug.LogError("[MiniDBSQL] Disconnect error: " + ex.Message);
            }
        }
        
        private async Task InitializeDatabase()
        {
            try
            {
                // Create database (ignore if exists)
                await SendQueryAndWait("CREATE DATABASE IF NOT EXISTS " + databaseName);
                await Task.Delay(100); // Small delay
                
                // Switch to database
                await SendQueryAndWait("USE DATABASE " + databaseName);
                await Task.Delay(100);
                
                // Create basic gaming tables
                await SendQueryAndWait(@"
                    CREATE TABLE IF NOT EXISTS game_sessions (
                        session_id TEXT PRIMARY KEY,
                        session_name TEXT NOT NULL,
                        max_players INTEGER DEFAULT 4,
                        current_players INTEGER DEFAULT 0,
                        game_state TEXT DEFAULT '{}',
                        created_at INTEGER DEFAULT (strftime('%s', 'now')),
                        created_by TEXT NOT NULL
                    )");
                await Task.Delay(100);
                
                await SendQueryAndWait(@"
                    CREATE TABLE IF NOT EXISTS game_events (
                        event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                        session_id TEXT NOT NULL,
                        player_id TEXT NOT NULL,
                        event_type TEXT NOT NULL,
                        event_data TEXT NOT NULL,
                        timestamp INTEGER DEFAULT (strftime('%s', 'now'))
                    )");
                
                if (enableDebugLogs)
                    Debug.Log("[MiniDBSQL] Database initialized");
            }
            catch (Exception ex)
            {
                Debug.LogError("[MiniDBSQL] Database initialization failed: " + ex.Message);
            }
        }
        
        public async Task<string> CreateGameSession(string sessionName, int maxPlayers = 4)
        {
            string sessionId = Guid.NewGuid().ToString();
            
            await SendQueryAndWait(string.Format(@"
                INSERT INTO game_sessions (session_id, session_name, max_players, created_by) 
                VALUES ('{0}', '{1}', {2}, '{3}')
            ", sessionId, sessionName, maxPlayers, playerId));
            
            return sessionId;
        }
        
        public async Task SendGameEvent(string sessionId, string eventType, object eventData)
        {
            string jsonData = JsonConvert.SerializeObject(eventData);
            
            await SendQueryAndWait(string.Format(@"
                INSERT INTO game_events (session_id, player_id, event_type, event_data) 
                VALUES ('{0}', '{1}', '{2}', '{3}')
            ", sessionId, playerId, eventType, jsonData));
        }
        
        public async Task UpdateGameState(string sessionId, object gameState)
        {
            string jsonState = JsonConvert.SerializeObject(gameState);
            
            await SendQueryAndWait(string.Format(@"
                UPDATE game_sessions 
                SET game_state = '{0}' 
                WHERE session_id = '{1}'
            ", jsonState, sessionId));
        }
        
        public async Task<List<JObject>> GetActiveSessions()
        {
            string response = await ExecuteQueryAsync("SELECT * FROM game_sessions ORDER BY created_at DESC LIMIT 10");
            return ParseJsonArrayResponse(response);
        }
        
        private List<JObject> ParseJsonArrayResponse(string response)
        {
            var results = new List<JObject>();
            
            try
            {
                // Try to parse as JSON response from server
                var jsonResponse = JObject.Parse(response);
                
                if (jsonResponse["results"] is JArray resultsArray)
                {
                    foreach (var item in resultsArray)
                    {
                        if (item is JObject obj)
                        {
                            results.Add(obj);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogWarning("[MiniDBSQL] Failed to parse JSON response: " + ex.Message);
            }
            
            return results;
        }
        
        public async Task<string> ExecuteQueryAsync(string query)
        {
            if (enableDebugLogs)
                Debug.Log($"[MiniDBSQL] ExecuteQueryAsync: {query}");
            
            return await SendQueryAndWait(query);
        }
        
        /// <summary>
        /// Subscribe to table changes for real-time notifications
        /// </summary>
        public async Task<bool> SubscribeToTable(string tableName)
        {
            try
            {
                if (enableDebugLogs)
                    Debug.Log($"[MiniDBSQL] Subscribing to table: {tableName}");
                
                string response = await ExecuteQueryAsync($"SUBSCRIBE {tableName}");
                
                if (enableDebugLogs)
                    Debug.Log($"[MiniDBSQL] Subscription response: {response}");
                
                // Check for ACK in the response
                bool success = response.Contains("ACK: SUBSCRIBE") || response.Contains("subscribed to table");
                
                if (enableDebugLogs)
                    Debug.Log($"[MiniDBSQL] Subscription {(success ? "SUCCESS" : "FAILED")} for table {tableName}");
                
                return success;
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogError($"[MiniDBSQL] Failed to subscribe to table {tableName}: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Unsubscribe from table changes
        /// </summary>
        public async Task<bool> UnsubscribeFromTable(string tableName)
        {
            try
            {
                string response = await ExecuteQueryAsync($"UNSUBSCRIBE {tableName}");
                
                if (enableDebugLogs)
                    Debug.Log($"[MiniDBSQL] Unsubscription response: {response}");
                
                return response.Contains("ACK: UNSUBSCRIBE");
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogError($"[MiniDBSQL] Failed to unsubscribe from table {tableName}: {ex.Message}");
                return false;
            }
        }
        
        private async Task<string> SendQueryAndWait(string query)
        {
            if (!IsConnected) 
                throw new InvalidOperationException("Not connected to MiniDB");
            
            // Use semaphore to ensure sequential query execution - prevents race conditions
            await querySemaphore.WaitAsync();
            
            try
            {
                // Validate query before sending
                if (string.IsNullOrEmpty(query) || query.Trim().Length == 0)
                {
                    Debug.LogError($"[MiniDBSQL] Empty query detected");
                    return "ERROR: Empty query intercepted by client";
                }
                
                if (enableDebugLogs)
                    Debug.Log($"[MiniDBSQL] Executing query: {query}");
                
                // Send query directly without ID prefix (MiniDB server expects pure SQL)
                var queryBytes = Encoding.UTF8.GetBytes(query);
                await webSocket.SendAsync(new ArraySegment<byte>(queryBytes), WebSocketMessageType.Text, true, cancellationTokenSource.Token);
                
                if (enableDebugLogs)
                    Debug.Log("[MiniDBSQL] Sent query: " + query);
                
                // Wait for response with a simple completion source
                var tcs = new TaskCompletionSource<string>();
                latestResponse = tcs; // Store reference for ProcessResponse
                
                // Wait for response with timeout
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(-1, timeoutCts.Token));
                    
                    if (completedTask == tcs.Task)
                    {
                        var result = await tcs.Task;
                        if (enableDebugLogs)
                            Debug.Log($"[MiniDBSQL] Query completed");
                        return result;
                    }
                    else
                    {
                        latestResponse = null;
                        throw new TimeoutException("Query timeout after 10 seconds");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[MiniDBSQL] Query error: " + ex.Message);
                throw;
            }
            finally
            {
                querySemaphore.Release();
            }
        }
        
        private async Task StartReceiving()
        {
            var buffer = new byte[8192];
            
            try
            {
                while (!cancellationTokenSource.Token.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationTokenSource.Token);
                    
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        
                        if (enableDebugLogs)
                            Debug.Log("[MiniDBSQL] Received: " + message);
                        
                        // Process the response
                        ProcessResponse(message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }
            catch (Exception ex)
            {
                Debug.LogError("[MiniDBSQL] Receive error: " + ex.Message);
                OnError?.Invoke(ex.Message);
            }
        }
        
        private void ProcessResponse(string response)
        {
            try
            {
                if (enableDebugLogs)
                    Debug.Log($"[MiniDBSQL] Processing response: {response}");

                // Skip welcome messages
                if (response.Contains("\"type\":\"welcome\"") || response.Contains("Welcome to Mini-DB"))
                {
                    if (enableDebugLogs)
                        Debug.Log("[MiniDBSQL] Welcome message received");
                    return;
                }
                
                // Handle subscription acknowledgments
                if (response.StartsWith("ACK: SUBSCRIBE") || response.StartsWith("ACK: UNSUBSCRIBE"))
                {
                    if (enableDebugLogs)
                        Debug.Log($"[MiniDBSQL] Subscription ACK: {response}");
                        
                    // Handle as query response if we're waiting for one
                    if (latestResponse != null)
                    {
                        latestResponse.TrySetResult(response);
                        latestResponse = null;
                    }
                    return;
                }
                
                // Handle query response
                if (latestResponse != null)
                {
                    if (enableDebugLogs)
                        Debug.Log($"[MiniDBSQL] Handling query response");
                    latestResponse.TrySetResult(response);
                    latestResponse = null;
                    return;
                }
                
                // Handle table notifications (real-time updates)
                if (response.Contains("\"table\":") && (response.Contains("\"notification\":") || response.Contains("\"type\":\"table_notification\"")))
                {
                    if (enableDebugLogs)
                        Debug.Log($"[MiniDBSQL] Table notification: {response}");
                    
                    OnTableNotification?.Invoke(response);
                    return;
                }
                
                // Handle modular system notifications (game.tictactoe format)
                if (response.Contains("\"session_id\":") && response.Contains("\"game_status\":"))
                {
                    if (enableDebugLogs)
                        Debug.Log($"[MiniDBSQL] Game notification: {response}");
                    
                    OnTableNotification?.Invoke(response);
                    return;
                }
                
                // Handle game-related notifications
                if (response.Contains("\"board_state\":") || (response.Contains("\"player_x\":") && response.Contains("\"player_o\":")))
                {
                    if (enableDebugLogs)
                        Debug.Log($"[MiniDBSQL] Game notification: {response}");
                    
                    OnTableNotification?.Invoke(response);
                    return;
                }
                
                // Other messages (broadcasts, etc.)
                if (enableDebugLogs)
                    Debug.Log("[MiniDBSQL] Broadcast: " + response);
            }
            catch (Exception ex)
            {
                Debug.LogError("[MiniDBSQL] Response processing error: " + ex.Message);
            }
        }
        
        private void OnDestroy()
        {
            _ = DisconnectAsync();
        }
    }
}