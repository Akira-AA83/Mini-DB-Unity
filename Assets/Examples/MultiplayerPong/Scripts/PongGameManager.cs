using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MiniDB.Unity.SQL;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace MiniDB.Unity.Examples.Pong
{
    /// <summary>
    /// Real-time multiplayer Pong game using MiniDB for state synchronization
    /// Demonstrates: Real-time game state sync + Physics + Multiplayer matchmaking
    /// </summary>
    public class PongGameManager : MonoBehaviour
    {
        [Header("Game Objects")]
        [SerializeField] private Transform leftPaddle;
        [SerializeField] private Transform rightPaddle;
        [SerializeField] private Transform ball;
        [SerializeField] private Transform gameArea;
        
        [Header("UI References")]
        [SerializeField] private TMP_Text leftScoreText;
        [SerializeField] private TMP_Text rightScoreText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text playerInfoText;
        [SerializeField] private Button joinGameButton;
        [SerializeField] private Button readyButton;
        
        [Header("Game Settings")]
        [SerializeField] private float paddleSpeed = 10f;
        [SerializeField] private float ballSpeed = 8f;
        [SerializeField] private float gameAreaWidth = 10f;
        [SerializeField] private float gameAreaHeight = 6f;
        [SerializeField] private int maxScore = 5;
        [SerializeField] private bool enableDebugLogs = true;
        
        private MiniDBSQLClient dbClient;
        private string gameSessionId;
        private string playerId;
        private string playerName;
        private PongPlayerSide playerSide = PongPlayerSide.None;
        private PongGameState gameState;
        private bool isGameActive = false;
        private bool isHost = false;
        
        // Game physics
        private Vector2 ballVelocity;
        private float paddleHalfHeight = 1f;
        private float ballRadius = 0.25f;
        
        // Events
        public event Action<PongGameState> OnGameStateChanged;
        public event Action<PongPlayerSide> OnPlayerJoined;
        public event Action<int, int> OnScoreChanged;
        
        private void Awake()
        {
            playerId = SystemInfo.deviceUniqueIdentifier;
            playerName = "Player_" + UnityEngine.Random.Range(1000, 9999);
            
            // Initialize game state
            gameState = new PongGameState
            {
                leftPaddleY = 0f,
                rightPaddleY = 0f,
                ballX = 0f,
                ballY = 0f,
                ballVelX = 0f,
                ballVelY = 0f,
                leftScore = 0,
                rightScore = 0,
                gameStatus = "waiting",
                lastUpdate = DateTime.Now
            };
            
            // Setup UI events
            joinGameButton.onClick.AddListener(() => _ = JoinGame());
            readyButton.onClick.AddListener(() => _ = SetPlayerReady());
        }
        
        private async void Start()
        {
            UpdateStatus("Connecting to game server...");
            await InitializeGame();
        }
        
        private async Task InitializeGame()
        {
            try
            {
                // Create and setup MiniDB client
                var clientGO = new GameObject("PongDBClient");
                clientGO.transform.SetParent(transform);
                dbClient = clientGO.AddComponent<MiniDBSQLClient>();
                
                // Subscribe to connection events
                dbClient.OnConnected += OnDatabaseConnected;
                dbClient.OnDisconnected += OnDatabaseDisconnected;
                dbClient.OnError += OnDatabaseError;
                
                // Connect to database
                bool connected = await dbClient.ConnectAsync();
                
                if (connected)
                {
                    await SetupGameDatabase();
                    await FindOrCreateGameSession();
                    
                    UpdateStatus("Connected - Ready to play!");
                    
                    // Start game state synchronization
                    StartGameSync();
                }
                else
                {
                    UpdateStatus("Failed to connect to game server");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Game initialization failed: {ex.Message}");
                if (enableDebugLogs)
                    Debug.LogError($"[PongGame] Initialization error: {ex}");
            }
        }
        
        private async Task SetupGameDatabase()
        {
            if (enableDebugLogs)
                Debug.Log("[PongGame] Setting up game database schema");
            
            try
            {
                // Create game database
                await dbClient.ExecuteQueryAsync("CREATE DATABASE IF NOT EXISTS pong_game");
                await dbClient.ExecuteQueryAsync("USE DATABASE pong_game");
                
                // Create game sessions table
                await dbClient.ExecuteQueryAsync(@"
                    CREATE TABLE IF NOT EXISTS game_sessions (
                        session_id TEXT PRIMARY KEY,
                        session_name TEXT NOT NULL,
                        max_players INTEGER DEFAULT 2,
                        current_players INTEGER DEFAULT 0,
                        game_state TEXT DEFAULT '{}',
                        status TEXT DEFAULT 'waiting',
                        created_at INTEGER DEFAULT (strftime('%s', 'now')),
                        updated_at INTEGER DEFAULT (strftime('%s', 'now'))
                    )");
                
                // Create players table
                await dbClient.ExecuteQueryAsync(@"
                    CREATE TABLE IF NOT EXISTS game_players (
                        player_id TEXT PRIMARY KEY,
                        session_id TEXT NOT NULL,
                        player_name TEXT NOT NULL,
                        player_side TEXT NOT NULL,
                        is_ready BOOLEAN DEFAULT FALSE,
                        last_ping INTEGER DEFAULT (strftime('%s', 'now')),
                        joined_at INTEGER DEFAULT (strftime('%s', 'now'))
                    )");
                
                // Create game events table for detailed tracking
                await dbClient.ExecuteQueryAsync(@"
                    CREATE TABLE IF NOT EXISTS game_events (
                        event_id INTEGER PRIMARY KEY AUTOINCREMENT,
                        session_id TEXT NOT NULL,
                        event_type TEXT NOT NULL,
                        event_data TEXT NOT NULL,
                        timestamp INTEGER DEFAULT (strftime('%s', 'now'))
                    )");
                
                if (enableDebugLogs)
                    Debug.Log("[PongGame] Game database schema ready");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PongGame] Database setup failed: {ex.Message}");
                throw;
            }
        }
        
        private async Task FindOrCreateGameSession()
        {
            try
            {
                // Try to find an existing waiting game
                string response = await dbClient.ExecuteQueryAsync(@"
                    SELECT session_id, current_players 
                    FROM game_sessions 
                    WHERE status = 'waiting' AND current_players < max_players
                    ORDER BY created_at ASC 
                    LIMIT 1
                ");
                
                var jsonResponse = JObject.Parse(response);
                if (jsonResponse["results"] is JArray results && results.Count > 0)
                {
                    // Join existing game
                    gameSessionId = results[0]["session_id"]?.ToString();
                    isHost = false;
                    
                    if (enableDebugLogs)
                        Debug.Log($"[PongGame] Joined existing game: {gameSessionId}");
                }
                else
                {
                    // Create new game session
                    gameSessionId = Guid.NewGuid().ToString();
                    isHost = true;
                    
                    await dbClient.ExecuteQueryAsync($@"
                        INSERT INTO game_sessions (session_id, session_name, current_players, status) 
                        VALUES ('{gameSessionId}', 'Pong Game {UnityEngine.Random.Range(1000, 9999)}', 0, 'waiting')
                    ");
                    
                    if (enableDebugLogs)
                        Debug.Log($"[PongGame] Created new game: {gameSessionId}");
                }
                
                UpdatePlayerInfo($"Session: {gameSessionId.Substring(0, 8)}...");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PongGame] Failed to find/create game session: {ex.Message}");
                throw;
            }
        }
        
        public async Task<bool> JoinGame()
        {
            if (playerSide != PongPlayerSide.None)
                return false; // Already joined
            
            try
            {
                // Check available positions
                string response = await dbClient.ExecuteQueryAsync($@"
                    SELECT player_side, COUNT(*) as count 
                    FROM game_players 
                    WHERE session_id = '{gameSessionId}' 
                    GROUP BY player_side
                ");
                
                var jsonResponse = JObject.Parse(response);
                bool leftTaken = false, rightTaken = false;
                
                if (jsonResponse["results"] is JArray results)
                {
                    foreach (var result in results)
                    {
                        string side = result["player_side"]?.ToString();
                        if (side == "left") leftTaken = true;
                        else if (side == "right") rightTaken = true;
                    }
                }
                
                // Assign player side
                if (!leftTaken)
                    playerSide = PongPlayerSide.Left;
                else if (!rightTaken)
                    playerSide = PongPlayerSide.Right;
                else
                    return false; // Game full
                
                // Insert player into database
                await dbClient.ExecuteQueryAsync($@"
                    INSERT INTO game_players (player_id, session_id, player_name, player_side, is_ready) 
                    VALUES ('{playerId}', '{gameSessionId}', '{playerName}', '{playerSide.ToString().ToLower()}', FALSE)
                ");
                
                // Update session player count
                await dbClient.ExecuteQueryAsync($@"
                    UPDATE game_sessions 
                    SET current_players = (SELECT COUNT(*) FROM game_players WHERE session_id = '{gameSessionId}'),
                        updated_at = strftime('%s', 'now')
                    WHERE session_id = '{gameSessionId}'
                ");
                
                UpdateStatus($"Joined as {playerSide} player");
                UpdatePlayerInfo($"Playing as: {playerSide}");
                OnPlayerJoined?.Invoke(playerSide);
                
                // Enable ready button
                readyButton.interactable = true;
                joinGameButton.interactable = false;
                
                if (enableDebugLogs)
                    Debug.Log($"[PongGame] Player joined as {playerSide}");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PongGame] Failed to join game: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> SetPlayerReady()
        {
            if (playerSide == PongPlayerSide.None)
                return false;
            
            try
            {
                await dbClient.ExecuteQueryAsync($@"
                    UPDATE game_players 
                    SET is_ready = TRUE, last_ping = strftime('%s', 'now') 
                    WHERE player_id = '{playerId}' AND session_id = '{gameSessionId}'
                ");
                
                // Check if both players are ready
                string response = await dbClient.ExecuteQueryAsync($@"
                    SELECT COUNT(*) as ready_count 
                    FROM game_players 
                    WHERE session_id = '{gameSessionId}' AND is_ready = TRUE
                ");
                
                var jsonResponse = JObject.Parse(response);
                if (jsonResponse["results"] is JArray results && results.Count > 0)
                {
                    int readyCount = int.Parse(results[0]["ready_count"]?.ToString() ?? "0");
                    
                    if (readyCount >= 2)
                    {
                        // Both players ready - start game
                        await StartGame();
                    }
                    else
                    {
                        UpdateStatus("Ready! Waiting for other player...");
                    }
                }
                
                readyButton.interactable = false;
                
                if (enableDebugLogs)
                    Debug.Log("[PongGame] Player marked as ready");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PongGame] Failed to set ready: {ex.Message}");
                return false;
            }
        }
        
        private async Task StartGame()
        {
            try
            {
                // Initialize game state
                gameState.ballX = 0f;
                gameState.ballY = 0f;
                gameState.ballVelX = UnityEngine.Random.Range(0, 2) == 0 ? ballSpeed : -ballSpeed;
                gameState.ballVelY = UnityEngine.Random.Range(-ballSpeed * 0.5f, ballSpeed * 0.5f);
                gameState.leftScore = 0;
                gameState.rightScore = 0;
                gameState.gameStatus = "playing";
                gameState.lastUpdate = DateTime.Now;
                
                ballVelocity = new Vector2(gameState.ballVelX, gameState.ballVelY);
                
                // Update session status
                await dbClient.ExecuteQueryAsync($@"
                    UPDATE game_sessions 
                    SET status = 'playing', 
                        game_state = '{EscapeJsonString(JsonUtility.ToJson(gameState))}',
                        updated_at = strftime('%s', 'now')
                    WHERE session_id = '{gameSessionId}'
                ");
                
                isGameActive = true;
                UpdateStatus("Game Started!");
                OnGameStateChanged?.Invoke(gameState);
                
                if (enableDebugLogs)
                    Debug.Log("[PongGame] Game started");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PongGame] Failed to start game: {ex.Message}");
            }
        }
        
        private void StartGameSync()
        {
            // Sync game state every 1/60 second for smooth gameplay
            InvokeRepeating(nameof(SyncGameState), 0f, 1f/60f);
        }
        
        private async void SyncGameState()
        {
            if (!isGameActive)
                return;
            
            try
            {
                if (isHost)
                {
                    // Host updates physics and uploads state
                    UpdateGamePhysics();
                    await UploadGameState();
                }
                else
                {
                    // Client downloads and applies state
                    await DownloadGameState();
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogWarning($"[PongGame] Sync error: {ex.Message}");
            }
        }
        
        private void UpdateGamePhysics()
        {
            if (!isGameActive) return;
            
            float deltaTime = Time.fixedDeltaTime;
            
            // Update ball position
            gameState.ballX += ballVelocity.x * deltaTime;
            gameState.ballY += ballVelocity.y * deltaTime;
            
            // Ball collision with top/bottom walls
            if (gameState.ballY > gameAreaHeight/2 - ballRadius || gameState.ballY < -gameAreaHeight/2 + ballRadius)
            {
                ballVelocity.y = -ballVelocity.y;
                gameState.ballY = Mathf.Clamp(gameState.ballY, -gameAreaHeight/2 + ballRadius, gameAreaHeight/2 - ballRadius);
            }
            
            // Ball collision with paddles
            CheckPaddleCollision();
            
            // Ball out of bounds (scoring)
            if (gameState.ballX > gameAreaWidth/2 + ballRadius)
            {
                // Left player scores
                gameState.leftScore++;
                OnScoreChanged?.Invoke(gameState.leftScore, gameState.rightScore);
                ResetBall(true); // Ball goes to right
            }
            else if (gameState.ballX < -gameAreaWidth/2 - ballRadius)
            {
                // Right player scores
                gameState.rightScore++;
                OnScoreChanged?.Invoke(gameState.leftScore, gameState.rightScore);
                ResetBall(false); // Ball goes to left
            }
            
            // Check win condition
            if (gameState.leftScore >= maxScore || gameState.rightScore >= maxScore)
            {
                _ = EndGame();
            }
            
            // Update velocity in state
            gameState.ballVelX = ballVelocity.x;
            gameState.ballVelY = ballVelocity.y;
            gameState.lastUpdate = DateTime.Now;
        }
        
        private void CheckPaddleCollision()
        {
            // Left paddle collision
            if (gameState.ballX - ballRadius <= -gameAreaWidth/2 + 0.2f && 
                ballVelocity.x < 0 &&
                gameState.ballY >= gameState.leftPaddleY - paddleHalfHeight &&
                gameState.ballY <= gameState.leftPaddleY + paddleHalfHeight)
            {
                ballVelocity.x = Mathf.Abs(ballVelocity.x);
                float hitPos = (gameState.ballY - gameState.leftPaddleY) / paddleHalfHeight;
                ballVelocity.y = hitPos * ballSpeed * 0.8f;
                gameState.ballX = -gameAreaWidth/2 + 0.2f + ballRadius;
            }
            
            // Right paddle collision
            if (gameState.ballX + ballRadius >= gameAreaWidth/2 - 0.2f && 
                ballVelocity.x > 0 &&
                gameState.ballY >= gameState.rightPaddleY - paddleHalfHeight &&
                gameState.ballY <= gameState.rightPaddleY + paddleHalfHeight)
            {
                ballVelocity.x = -Mathf.Abs(ballVelocity.x);
                float hitPos = (gameState.ballY - gameState.rightPaddleY) / paddleHalfHeight;
                ballVelocity.y = hitPos * ballSpeed * 0.8f;
                gameState.ballX = gameAreaWidth/2 - 0.2f - ballRadius;
            }
        }
        
        private void ResetBall(bool goRight)
        {
            gameState.ballX = 0f;
            gameState.ballY = 0f;
            ballVelocity.x = (goRight ? ballSpeed : -ballSpeed);
            ballVelocity.y = UnityEngine.Random.Range(-ballSpeed * 0.5f, ballSpeed * 0.5f);
        }
        
        private async Task UploadGameState()
        {
            try
            {
                string stateJson = JsonUtility.ToJson(gameState);
                await dbClient.ExecuteQueryAsync($@"
                    UPDATE game_sessions 
                    SET game_state = '{EscapeJsonString(stateJson)}',
                        updated_at = strftime('%s', 'now')
                    WHERE session_id = '{gameSessionId}'
                ");
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogWarning($"[PongGame] Upload state failed: {ex.Message}");
            }
        }
        
        private async Task DownloadGameState()
        {
            try
            {
                string response = await dbClient.ExecuteQueryAsync($@"
                    SELECT game_state 
                    FROM game_sessions 
                    WHERE session_id = '{gameSessionId}'
                ");
                
                var jsonResponse = JObject.Parse(response);
                if (jsonResponse["results"] is JArray results && results.Count > 0)
                {
                    string stateJson = results[0]["game_state"]?.ToString();
                    if (!string.IsNullOrEmpty(stateJson))
                    {
                        var newState = JsonUtility.FromJson<PongGameState>(stateJson);
                        if (newState != null)
                        {
                            gameState = newState;
                            ballVelocity = new Vector2(gameState.ballVelX, gameState.ballVelY);
                            OnGameStateChanged?.Invoke(gameState);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogWarning($"[PongGame] Download state failed: {ex.Message}");
            }
        }
        
        private async Task EndGame()
        {
            try
            {
                isGameActive = false;
                gameState.gameStatus = "finished";
                
                string winner = gameState.leftScore >= maxScore ? "Left" : "Right";
                UpdateStatus($"Game Over! {winner} player wins!");
                
                // Update session
                await dbClient.ExecuteQueryAsync($@"
                    UPDATE game_sessions 
                    SET status = 'finished',
                        game_state = '{EscapeJsonString(JsonUtility.ToJson(gameState))}',
                        updated_at = strftime('%s', 'now')
                    WHERE session_id = '{gameSessionId}'
                ");
                
                // Log game event
                await dbClient.ExecuteQueryAsync($@"
                    INSERT INTO game_events (session_id, event_type, event_data) 
                    VALUES ('{gameSessionId}', 'game_end', 
                    '{{""winner"":""{winner}"",""score"":""{gameState.leftScore}-{gameState.rightScore}""}}')
                ");
                
                if (enableDebugLogs)
                    Debug.Log($"[PongGame] Game ended. Winner: {winner}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PongGame] Failed to end game: {ex.Message}");
            }
        }
        
        private void Update()
        {
            if (!isGameActive || playerSide == PongPlayerSide.None)
                return;
            
            // Handle player input
            float input = 0f;
            if (playerSide == PongPlayerSide.Left)
            {
                if (Input.GetKey(KeyCode.W)) input = 1f;
                if (Input.GetKey(KeyCode.S)) input = -1f;
            }
            else if (playerSide == PongPlayerSide.Right)
            {
                if (Input.GetKey(KeyCode.UpArrow)) input = 1f;
                if (Input.GetKey(KeyCode.DownArrow)) input = -1f;
            }
            
            // Update paddle position
            if (input != 0f)
            {
                float newY = (playerSide == PongPlayerSide.Left ? gameState.leftPaddleY : gameState.rightPaddleY) + 
                            input * paddleSpeed * Time.deltaTime;
                
                newY = Mathf.Clamp(newY, -gameAreaHeight/2 + paddleHalfHeight, gameAreaHeight/2 - paddleHalfHeight);
                
                if (playerSide == PongPlayerSide.Left)
                    gameState.leftPaddleY = newY;
                else
                    gameState.rightPaddleY = newY;
                
                _ = UpdatePlayerPosition();
            }
            
            // Update visual positions
            UpdateVisuals();
        }
        
        private async Task UpdatePlayerPosition()
        {
            try
            {
                float paddleY = playerSide == PongPlayerSide.Left ? gameState.leftPaddleY : gameState.rightPaddleY;
                
                await dbClient.ExecuteQueryAsync($@"
                    UPDATE game_players 
                    SET last_ping = strftime('%s', 'now') 
                    WHERE player_id = '{playerId}' AND session_id = '{gameSessionId}'
                ");
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogWarning($"[PongGame] Failed to update position: {ex.Message}");
            }
        }
        
        private void UpdateVisuals()
        {
            if (leftPaddle != null)
                leftPaddle.position = new Vector3(-gameAreaWidth/2 + 0.2f, gameState.leftPaddleY, 0);
            
            if (rightPaddle != null)
                rightPaddle.position = new Vector3(gameAreaWidth/2 - 0.2f, gameState.rightPaddleY, 0);
            
            if (ball != null)
                ball.position = new Vector3(gameState.ballX, gameState.ballY, 0);
            
            if (leftScoreText != null)
                leftScoreText.text = gameState.leftScore.ToString();
            
            if (rightScoreText != null)
                rightScoreText.text = gameState.rightScore.ToString();
        }
        
        private void UpdateStatus(string status)
        {
            if (statusText != null)
                statusText.text = status;
            
            if (enableDebugLogs)
                Debug.Log($"[PongGame] Status: {status}");
        }
        
        private void UpdatePlayerInfo(string info)
        {
            if (playerInfoText != null)
                playerInfoText.text = info;
        }
        
        private string EscapeJsonString(string input)
        {
            return input.Replace("\"", "\\\"").Replace("'", "''");
        }
        
        private void OnDatabaseConnected()
        {
            if (enableDebugLogs)
                Debug.Log("[PongGame] Database connected");
        }
        
        private void OnDatabaseDisconnected(string reason)
        {
            UpdateStatus($"Disconnected: {reason}");
            isGameActive = false;
            
            if (enableDebugLogs)
                Debug.Log($"[PongGame] Database disconnected: {reason}");
        }
        
        private void OnDatabaseError(string error)
        {
            UpdateStatus($"Error: {error}");
            
            if (enableDebugLogs)
                Debug.LogError($"[PongGame] Database error: {error}");
        }
        
        private async void OnDestroy()
        {
            if (dbClient != null && !string.IsNullOrEmpty(gameSessionId))
            {
                try
                {
                    // Remove player from session
                    await dbClient.ExecuteQueryAsync($@"
                        DELETE FROM game_players 
                        WHERE player_id = '{playerId}' AND session_id = '{gameSessionId}'
                    ");
                    
                    // Update session player count
                    await dbClient.ExecuteQueryAsync($@"
                        UPDATE game_sessions 
                        SET current_players = (SELECT COUNT(*) FROM game_players WHERE session_id = '{gameSessionId}'),
                            updated_at = strftime('%s', 'now')
                        WHERE session_id = '{gameSessionId}'
                    ");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PongGame] Cleanup failed: {ex.Message}");
                }
                
                await dbClient.DisconnectAsync();
            }
        }
    }
    
    [System.Serializable]
    public class PongGameState
    {
        public float leftPaddleY;
        public float rightPaddleY;
        public float ballX;
        public float ballY;
        public float ballVelX;
        public float ballVelY;
        public int leftScore;
        public int rightScore;
        public string gameStatus;
        public DateTime lastUpdate;
    }
    
    public enum PongPlayerSide
    {
        None,
        Left,
        Right
    }
}