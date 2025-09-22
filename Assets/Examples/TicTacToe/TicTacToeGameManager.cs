using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MiniDB.Unity.SQL;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace MiniDB.Unity.Examples.TicTacToe
{
    /// <summary>
    /// Turn-based TicTacToe multiplayer game using MiniDB for state synchronization
    /// Demonstrates: Turn-based gameplay + Move validation + Win conditions + Matchmaking
    /// </summary>
    public class TicTacToeGameManager : MonoBehaviour
    {
        [Header("Game Board")]
        [SerializeField] private Button[] boardButtons = new Button[9]; // 3x3 grid
        [SerializeField] private TMP_Text[] cellTexts = new TMP_Text[9];
        
        [Header("UI References")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text playerInfoText;
        [SerializeField] private TMP_Text turnIndicatorText;
        [SerializeField] private Button joinGameButton;
        [SerializeField] private Button newGameButton;
        [SerializeField] private Button leaveGameButton;
        
        [Header("Game Settings")]
        [SerializeField] private Color playerXColor = Color.blue;
        [SerializeField] private Color playerOColor = Color.red;
        [SerializeField] private Color neutralColor = Color.gray;
        [SerializeField] private bool enableDebugLogs = true;
        
        private MiniDBSQLClient dbClient;
        private string gameSessionId;
        private string playerId;
        private string playerName;
        private TicTacToePlayerSymbol playerSymbol = TicTacToePlayerSymbol.None;
        private TicTacToeGameState gameState;
        private bool isMyTurn = false;
        private bool isGameActive = false;
        private bool isLoadingGameState = false;
        
        // Events
        public event Action<TicTacToeGameState> OnGameStateChanged;
        public event Action<TicTacToePlayerSymbol> OnPlayerJoined;
        public event Action<TicTacToePlayerSymbol> OnGameEnded;
        
        private void Awake()
        {
            // Generate unique player identity
            playerId = System.Guid.NewGuid().ToString();
            playerName = "Player_" + UnityEngine.Random.Range(1000, 9999);
            
            // Initialize game state
            gameState = new TicTacToeGameState
            {
                board = new string[9] { "", "", "", "", "", "", "", "", "" },
                currentTurn = "X",
                gameStatus = "waiting",
                playerX = "",
                playerO = "",
                winner = "",
                lastMoveTime = DateTime.Now
            };
            
            // Setup UI events
            joinGameButton.onClick.AddListener(() => _ = JoinGame());
            newGameButton.onClick.AddListener(() => _ = CreateNewGame());
            leaveGameButton.onClick.AddListener(() => _ = LeaveGame());
            
            // Setup board button events
            for (int i = 0; i < boardButtons.Length; i++)
            {
                int cellIndex = i; // Capture for closure
                boardButtons[i].onClick.AddListener(() => _ = MakeMove(cellIndex));
            }
            
            if (enableDebugLogs)
                Debug.Log($"[TicTacToe] Generated player: {playerName} (ID: {playerId})");
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
                var clientGO = new GameObject("TicTacToeDBClient");
                clientGO.transform.SetParent(transform);
                dbClient = clientGO.AddComponent<MiniDBSQLClient>();
                
                // Subscribe to connection events
                dbClient.OnConnected += OnDatabaseConnected;
                dbClient.OnDisconnected += OnDatabaseDisconnected;
                dbClient.OnError += OnDatabaseError;
                dbClient.OnTableNotification += OnTableNotification;
                
                // Connect to database
                bool connected = await dbClient.ConnectAsync();
                
                if (connected)
                {
                    await SetupGameDatabase();
                    
                    UpdateStatus("Connected - Ready to play!");
                    
                    // Enable UI
                    joinGameButton.interactable = true;
                    newGameButton.interactable = true;
                    
                    // Start listening for game updates
                    await SubscribeToGameUpdates();
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
                    Debug.LogError($"[TicTacToe] Initialization error: {ex}");
            }
        }
        
        private async Task SetupGameDatabase()
        {
            if (enableDebugLogs)
                Debug.Log("[TicTacToe] Setting up game database schema");
            
            try
            {
                // Create game database
                await dbClient.ExecuteQueryAsync("CREATE DATABASE IF NOT EXISTS gaming");
                await dbClient.ExecuteQueryAsync("USE DATABASE gaming");
                
                // Create game sessions table
                await dbClient.ExecuteQueryAsync(@"
                    CREATE TABLE IF NOT EXISTS ttt_sessions (
                        session_id TEXT PRIMARY KEY,
                        session_name TEXT NOT NULL,
                        player_x TEXT DEFAULT '',
                        player_o TEXT DEFAULT '',
                        current_turn TEXT DEFAULT 'X',
                        board_state TEXT DEFAULT '[]',
                        game_status TEXT DEFAULT 'waiting',
                        winner TEXT DEFAULT '',
                        created_at INTEGER DEFAULT (strftime('%s', 'now')),
                        updated_at INTEGER DEFAULT (strftime('%s', 'now'))
                    )");
                
                // Create game moves table for history
                await dbClient.ExecuteQueryAsync(@"
                    CREATE TABLE IF NOT EXISTS ttt_moves (
                        move_id INTEGER PRIMARY KEY AUTOINCREMENT,
                        session_id TEXT NOT NULL,
                        player_id TEXT NOT NULL,
                        player_symbol TEXT NOT NULL,
                        cell_position INTEGER NOT NULL,
                        move_timestamp INTEGER DEFAULT (strftime('%s', 'now'))
                    )");
                
                if (enableDebugLogs)
                    Debug.Log("[TicTacToe] Game database schema ready");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TicTacToe] Database setup failed: {ex.Message}");
                throw;
            }
        }
        
        private async Task SubscribeToGameUpdates()
        {
            try
            {
                if (enableDebugLogs)
                    Debug.Log("[TicTacToe] Subscribing to game table updates...");
                
                bool subscribed = await dbClient.SubscribeToTable("ttt_sessions");
                
                if (subscribed)
                {
                    if (enableDebugLogs)
                        Debug.Log("[TicTacToe] Successfully subscribed to real-time game updates");
                }
                else
                {
                    if (enableDebugLogs)
                        Debug.LogError("[TicTacToe] Failed to subscribe to game updates");
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogError($"[TicTacToe] Subscription error: {ex.Message}");
            }
        }
        
        public async Task<bool> CreateNewGame()
        {
            try
            {
                gameSessionId = System.Guid.NewGuid().ToString();
                
                await dbClient.ExecuteQueryAsync($@"
                    INSERT INTO ttt_sessions (session_id, session_name, player_x) 
                    VALUES ('{gameSessionId}', 'TicTacToe Game {UnityEngine.Random.Range(1000, 9999)}', '{playerId}')
                ");
                
                // Set player as X (first player)
                playerSymbol = TicTacToePlayerSymbol.X;
                isMyTurn = false; // X goes first, but wait for opponent to join first
                isGameActive = false; // Game not active until opponent joins
                
                UpdateStatus("Game created! Waiting for opponent...");
                UpdatePlayerInfo($"You are X | Session: {gameSessionId.Substring(0, 8)}...");
                
                // Update UI
                joinGameButton.interactable = false;
                newGameButton.interactable = false;
                leaveGameButton.interactable = true;
                
                if (enableDebugLogs)
                    Debug.Log($"[TicTacToe] Created new game: {gameSessionId}");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TicTacToe] Failed to create game: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> JoinGame()
        {
            try
            {
                // Find an available game (waiting for player O)
                string response = await dbClient.ExecuteQueryAsync(@"
                    SELECT session_id, player_x 
                    FROM ttt_sessions 
                    WHERE game_status = 'waiting' AND player_o = '' 
                    ORDER BY created_at DESC 
                    LIMIT 1
                ");
                
                var jsonResponse = JObject.Parse(response);
                if (jsonResponse["results"] is JArray results && results.Count > 0)
                {
                    gameSessionId = results[0]["session_id"]?.ToString();
                    
                    // Join as player O
                    await dbClient.ExecuteQueryAsync($@"
                        UPDATE ttt_sessions 
                        SET player_o = '{playerId}', 
                            game_status = 'playing',
                            updated_at = strftime('%s', 'now')
                        WHERE session_id = '{gameSessionId}'
                    ");
                    
                    playerSymbol = TicTacToePlayerSymbol.O;
                    isMyTurn = false; // X goes first
                    isGameActive = true;
                    
                    UpdateStatus("Joined game! X player's turn");
                    UpdatePlayerInfo($"You are O | Session: {gameSessionId.Substring(0, 8)}...");
                    
                    // Update UI
                    joinGameButton.interactable = false;
                    newGameButton.interactable = false;
                    leaveGameButton.interactable = true;
                    
                    // Load current game state
                    await LoadGameState();
                    
                    if (enableDebugLogs)
                        Debug.Log($"[TicTacToe] Joined game: {gameSessionId} as O");
                    
                    return true;
                }
                else
                {
                    UpdateStatus("No available games. Create a new one!");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TicTacToe] Failed to join game: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> MakeMove(int cellPosition)
        {
            if (enableDebugLogs)
                Debug.Log($"[TicTacToe] MakeMove: cell {cellPosition}, active: {isGameActive}, turn: {isMyTurn}");
            
            if (!isGameActive || !isMyTurn || cellPosition < 0 || cellPosition >= 9)
            {
                if (enableDebugLogs)
                    Debug.Log($"[TicTacToe] MakeMove blocked - not allowed");
                return false;
            }
            
            // Check if cell is already occupied
            if (!string.IsNullOrEmpty(gameState.board[cellPosition]))
                return false;
            
            string symbolStr = playerSymbol.ToString();
            
            try
            {
                
                // Update local state immediately for responsiveness
                gameState.board[cellPosition] = symbolStr;
                gameState.currentTurn = playerSymbol == TicTacToePlayerSymbol.X ? "O" : "X";
                gameState.lastMoveTime = DateTime.Now;
                
                UpdateBoardVisuals();
                isMyTurn = false;
                
                // Save move to database
                await dbClient.ExecuteQueryAsync($@"
                    INSERT INTO ttt_moves (session_id, player_id, player_symbol, cell_position) 
                    VALUES ('{gameSessionId}', '{playerId}', '{symbolStr}', {cellPosition})
                ");
                
                // Update game session with new board state
                string boardJson = JsonConvert.SerializeObject(gameState.board);
                // Escape single quotes for SQL
                boardJson = boardJson.Replace("'", "''");
                
                await dbClient.ExecuteQueryAsync($@"
                    UPDATE ttt_sessions 
                    SET board_state = '{boardJson}',
                        current_turn = '{gameState.currentTurn}',
                        updated_at = strftime('%s', 'now')
                    WHERE session_id = '{gameSessionId}'
                ");
                
                // Check for win condition
                string winner = CheckWinCondition(gameState.board);
                if (!string.IsNullOrEmpty(winner) || IsBoardFull(gameState.board))
                {
                    await EndGame(winner);
                }
                
                UpdateTurnIndicator();
                
                if (enableDebugLogs)
                    Debug.Log($"[TicTacToe] Move made: {symbolStr} at position {cellPosition}");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TicTacToe] Failed to make move: {ex.Message}");
                
                // Revert local state on error
                gameState.board[cellPosition] = "";
                gameState.currentTurn = symbolStr;
                UpdateBoardVisuals();
                isMyTurn = true;
                
                return false;
            }
        }
        
        private async Task LoadGameState()
        {
            // Prevent multiple simultaneous LoadGameState calls
            if (isLoadingGameState)
            {
                if (enableDebugLogs)
                    Debug.Log("[TicTacToe] LoadGameState already in progress, skipping");
                return;
            }
            
            isLoadingGameState = true;
            
            try
            {
                string response = await dbClient.ExecuteQueryAsync($@"
                    SELECT board_state, current_turn, game_status, winner, player_x, player_o
                    FROM ttt_sessions 
                    WHERE session_id = '{gameSessionId}'
                ");
                
                var jsonResponse = JObject.Parse(response);
                if (jsonResponse["results"] is JArray results && results.Count > 0)
                {
                    var result = results[0];
                    
                    // Parse board state
                    string boardJson = result["board_state"]?.ToString();
                    if (!string.IsNullOrEmpty(boardJson) && boardJson != "[]")
                    {
                        try
                        {
                            // Use proper JSON deserialization
                            string[] deserializedBoard = JsonConvert.DeserializeObject<string[]>(boardJson);
                            if (deserializedBoard != null && deserializedBoard.Length >= 9)
                            {
                                for (int i = 0; i < 9; i++)
                                {
                                    gameState.board[i] = deserializedBoard[i] ?? "";
                                }
                            }
                        }
                        catch (JsonException ex)
                        {
                            if (enableDebugLogs)
                                Debug.LogWarning($"[TicTacToe] Failed to parse board_state JSON: {ex.Message}, falling back to manual parsing");
                            
                            // Fallback to legacy parsing for backward compatibility
                            boardJson = boardJson.Replace("'", "\"").Trim('[', ']'); 
                            string[] boardParts = boardJson.Split(',');
                            for (int i = 0; i < 9 && i < boardParts.Length; i++)
                            {
                                gameState.board[i] = boardParts[i].Trim('"', ' ');
                            }
                        }
                    }
                    
                    gameState.currentTurn = result["current_turn"]?.ToString() ?? "X";
                    gameState.gameStatus = result["game_status"]?.ToString() ?? "waiting";
                    gameState.winner = result["winner"]?.ToString() ?? "";
                    gameState.playerX = result["player_x"]?.ToString() ?? "";
                    gameState.playerO = result["player_o"]?.ToString() ?? "";
                    
                    // Update turn state
                    bool wasMyTurn = isMyTurn;
                    bool wasGameActive = isGameActive;
                    
                    isGameActive = gameState.gameStatus == "playing";
                    
                    // Set turn - X always goes first when game becomes active
                    isMyTurn = (gameState.currentTurn == "X" && playerSymbol == TicTacToePlayerSymbol.X) ||
                              (gameState.currentTurn == "O" && playerSymbol == TicTacToePlayerSymbol.O);
                    
                    if (enableDebugLogs)
                        Debug.Log($"[TicTacToe] LoadGameState: Status={gameState.gameStatus}, Turn={gameState.currentTurn}, Active={isGameActive}, MyTurn={isMyTurn}");
                    
                    // Ensure UI updates happen on main thread
                    if (UnityEngine.Application.isPlaying)
                    {
                        UpdateBoardVisuals();
                        UpdateTurnIndicator();
                        OnGameStateChanged?.Invoke(gameState);
                        
                        if (enableDebugLogs)
                            Debug.Log($"[TicTacToe] UI components updated");
                    }
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogError($"[TicTacToe] Failed to load game state: {ex.Message}");
            }
            finally
            {
                isLoadingGameState = false;
            }
        }
        
        private string CheckWinCondition(string[] board)
        {
            // Win patterns for 3x3 board
            int[,] winPatterns = {
                {0,1,2}, {3,4,5}, {6,7,8}, // Rows
                {0,3,6}, {1,4,7}, {2,5,8}, // Columns  
                {0,4,8}, {2,4,6}           // Diagonals
            };
            
            for (int i = 0; i < winPatterns.GetLength(0); i++)
            {
                int a = winPatterns[i, 0];
                int b = winPatterns[i, 1]; 
                int c = winPatterns[i, 2];
                
                if (!string.IsNullOrEmpty(board[a]) && 
                    board[a] == board[b] && 
                    board[b] == board[c])
                {
                    return board[a]; // Return winning symbol
                }
            }
            
            return ""; // No winner
        }
        
        private bool IsBoardFull(string[] board)
        {
            for (int i = 0; i < board.Length; i++)
            {
                if (string.IsNullOrEmpty(board[i]))
                    return false;
            }
            return true;
        }
        
        private async Task EndGame(string winner)
        {
            try
            {
                isGameActive = false;
                gameState.winner = winner;
                gameState.gameStatus = "finished";
                
                string statusMessage;
                if (string.IsNullOrEmpty(winner))
                {
                    statusMessage = "Game ended in a draw!";
                }
                else if (winner == playerSymbol.ToString())
                {
                    statusMessage = "You won!";
                }
                else
                {
                    statusMessage = "You lost!";
                }
                
                UpdateStatus(statusMessage);
                
                // Update database
                await dbClient.ExecuteQueryAsync($@"
                    UPDATE ttt_sessions 
                    SET game_status = 'finished',
                        winner = '{winner}',
                        updated_at = strftime('%s', 'now')
                    WHERE session_id = '{gameSessionId}'
                ");
                
                // Enable new game button
                newGameButton.interactable = true;
                leaveGameButton.interactable = false;
                
                OnGameEnded?.Invoke(string.IsNullOrEmpty(winner) ? TicTacToePlayerSymbol.None : 
                    (winner == "X" ? TicTacToePlayerSymbol.X : TicTacToePlayerSymbol.O));
                
                if (enableDebugLogs)
                    Debug.Log($"[TicTacToe] Game ended. Winner: {(string.IsNullOrEmpty(winner) ? "Draw" : winner)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TicTacToe] Failed to end game: {ex.Message}");
            }
        }
        
        public async Task<bool> LeaveGame()
        {
            try
            {
                if (!string.IsNullOrEmpty(gameSessionId))
                {
                    // Mark game as abandoned
                    await dbClient.ExecuteQueryAsync($@"
                        UPDATE ttt_sessions 
                        SET game_status = 'abandoned',
                            updated_at = strftime('%s', 'now')
                        WHERE session_id = '{gameSessionId}'
                    ");
                }
                
                // Reset local state
                gameSessionId = "";
                playerSymbol = TicTacToePlayerSymbol.None;
                isMyTurn = false;
                isGameActive = false;
                
                // Reset board
                gameState.board = new string[9] { "", "", "", "", "", "", "", "", "" };
                UpdateBoardVisuals();
                
                UpdateStatus("Left game. Ready to play!");
                UpdatePlayerInfo("");
                
                // Reset UI
                joinGameButton.interactable = true;
                newGameButton.interactable = true;
                leaveGameButton.interactable = false;
                
                if (enableDebugLogs)
                    Debug.Log("[TicTacToe] Left game");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TicTacToe] Failed to leave game: {ex.Message}");
                return false;
            }
        }
        
        private void UpdateBoardVisuals()
        {
            for (int i = 0; i < 9; i++)
            {
                if (cellTexts[i] != null)
                {
                    cellTexts[i].text = gameState.board[i];
                    
                    // Set colors
                    if (gameState.board[i] == "X")
                        cellTexts[i].color = playerXColor;
                    else if (gameState.board[i] == "O")
                        cellTexts[i].color = playerOColor;
                    else
                        cellTexts[i].color = neutralColor;
                }
                
                // Enable/disable buttons
                if (boardButtons[i] != null)
                {
                    boardButtons[i].interactable = isGameActive && isMyTurn && 
                        string.IsNullOrEmpty(gameState.board[i]);
                }
            }
        }
        
        private void UpdateTurnIndicator()
        {
            if (turnIndicatorText != null)
            {
                if (!isGameActive)
                {
                    turnIndicatorText.text = "";
                }
                else if (isMyTurn)
                {
                    turnIndicatorText.text = $"Your turn ({playerSymbol})";
                    turnIndicatorText.color = playerSymbol == TicTacToePlayerSymbol.X ? playerXColor : playerOColor;
                }
                else
                {
                    turnIndicatorText.text = "Opponent's turn";
                    turnIndicatorText.color = neutralColor;
                }
            }
        }
        
        private void UpdateStatus(string status)
        {
            if (statusText != null)
                statusText.text = status;
            
            if (enableDebugLogs)
                Debug.Log($"[TicTacToe] Status: {status}");
        }
        
        private void UpdatePlayerInfo(string info)
        {
            if (playerInfoText != null)
                playerInfoText.text = info;
        }
        
        /// <summary>
        /// Handle real-time table notifications from WebSocket
        /// </summary>
        private async void OnTableNotification(string notification)
        {
            try
            {
                if (enableDebugLogs)
                    Debug.Log($"[TicTacToe] Received table notification: {notification}");
                
                var jsonNotification = JObject.Parse(notification);
                
                // Check if this is a standard ttt_sessions notification OR a modular notification with session_id
                bool isTttSessionsNotification = jsonNotification["table"]?.ToString() == "ttt_sessions" ||
                                                 (jsonNotification["session_id"] != null && jsonNotification["game_status"] != null);
                
                if (isTttSessionsNotification)
                {
                    JObject updateData;
                    
                    // Handle both standard table notifications and modular notifications  
                    var dataField = jsonNotification["data"];
                    if (dataField != null)
                    {
                        // Standard table notification format: {"table":"ttt_sessions","data":{...}}
                        if (dataField.Type == JTokenType.String)
                        {
                            updateData = JObject.Parse(dataField.ToString());
                        }
                        else
                        {
                            updateData = (JObject)dataField;
                        }
                    }
                    else
                    {
                        // Modular notification format: {"session_id":"...","game_status":"...",...}
                        updateData = jsonNotification;
                    }
                    
                    string updatedSessionId = updateData["session_id"]?.ToString();
                    string updatedGameStatus = updateData["game_status"]?.ToString();
                    string updatedPlayerX = updateData["player_x"]?.ToString();
                    string updatedPlayerO = updateData["player_o"]?.ToString();
                    string updatedCurrentTurn = updateData["current_turn"]?.ToString();
                    string updatedBoardState = updateData["board_state"]?.ToString();
                    
                    if (enableDebugLogs)
                    {
                        Debug.Log($"[TicTacToe] Notification - Session: {updatedSessionId}, Status: {updatedGameStatus}");
                        Debug.Log($"[TicTacToe] Players X: {updatedPlayerX}, O: {updatedPlayerO}, Turn: {updatedCurrentTurn}");
                    }
                    
                    // Only process updates for our current game
                    if (updatedSessionId == gameSessionId)
                    {
                        if (enableDebugLogs)
                            Debug.Log("[TicTacToe] Processing game update for our session");
                        
                        // Use notification data directly if available (more reliable than re-querying)
                        if (!string.IsNullOrEmpty(updatedPlayerO) || !string.IsNullOrEmpty(updatedPlayerX))
                        {
                            if (enableDebugLogs)
                                Debug.Log("[TicTacToe] Using notification data to update game state");
                            
                            // Update game state from notification data
                            gameState.gameStatus = updatedGameStatus ?? gameState.gameStatus;
                            gameState.currentTurn = updatedCurrentTurn ?? gameState.currentTurn;
                            gameState.playerX = updatedPlayerX ?? gameState.playerX;
                            gameState.playerO = updatedPlayerO ?? gameState.playerO;
                            
                            // Update board state from notification
                            if (!string.IsNullOrEmpty(updatedBoardState))
                            {
                                try
                                {
                                    string[] updatedBoard = JsonConvert.DeserializeObject<string[]>(updatedBoardState);
                                    if (updatedBoard != null && updatedBoard.Length >= 9)
                                    {
                                        for (int i = 0; i < 9; i++)
                                        {
                                            gameState.board[i] = updatedBoard[i] ?? "";
                                        }
                                        
                                        if (enableDebugLogs)
                                            Debug.Log($"[TicTacToe] Board updated: [{string.Join(",", gameState.board)}]");
                                    }
                                }
                                catch (JsonException ex)
                                {
                                    if (enableDebugLogs)
                                        Debug.LogWarning($"[TicTacToe] Failed to parse board_state from notification: {ex.Message}");
                                }
                            }
                            
                            // Update local state
                            isGameActive = gameState.gameStatus == "playing";
                            isMyTurn = (gameState.currentTurn == "X" && playerSymbol == TicTacToePlayerSymbol.X) ||
                                      (gameState.currentTurn == "O" && playerSymbol == TicTacToePlayerSymbol.O);
                            
                            // Update UI
                            UpdateBoardVisuals();
                            UpdateTurnIndicator();
                            OnGameStateChanged?.Invoke(gameState);
                            
                            if (enableDebugLogs)
                                Debug.Log($"[TicTacToe] Updated - Status: {gameState.gameStatus}, Turn: {gameState.currentTurn}, Active: {isGameActive}");
                        }
                        else
                        {
                            // Fallback to querying database with longer delay and retry
                            await Task.Delay(250);
                            
                            // Reload game state from database with retry
                            for (int retry = 0; retry < 3; retry++)
                            {
                                await LoadGameState();
                                
                                // Check if we got consistent data
                                if (gameState.gameStatus == "playing" && !string.IsNullOrEmpty(gameState.playerO))
                                {
                                    if (enableDebugLogs)
                                        Debug.Log($"[TicTacToe] LoadGameState succeeded on retry {retry + 1}");
                                    break;
                                }
                                
                                if (retry < 2)
                                {
                                    if (enableDebugLogs)
                                        Debug.Log($"[TicTacToe] LoadGameState retry {retry + 1}");
                                    await Task.Delay(200);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (enableDebugLogs)
                            Debug.Log("[TicTacToe] Ignoring notification for different session");
                    }
                }
                else
                {
                    if (enableDebugLogs)
                        Debug.Log($"[TicTacToe] Ignoring notification for table: {jsonNotification["table"]?.ToString()}");
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogError($"[TicTacToe] Error processing table notification: {ex.Message}\nNotification: {notification}");
            }
        }
        
        private void OnDatabaseConnected()
        {
            if (enableDebugLogs)
                Debug.Log("[TicTacToe] Database connected");
        }
        
        private void OnDatabaseDisconnected(string reason)
        {
            UpdateStatus($"Disconnected: {reason}");
            isGameActive = false;
            
            if (enableDebugLogs)
                Debug.Log($"[TicTacToe] Database disconnected: {reason}");
        }
        
        private void OnDatabaseError(string error)
        {
            UpdateStatus($"Error: {error}");
            
            if (enableDebugLogs)
                Debug.LogError($"[TicTacToe] Database error: {error}");
        }
        
        private async void OnDestroy()
        {
            if (dbClient != null && !string.IsNullOrEmpty(gameSessionId))
            {
                try
                {
                    await LeaveGame();
                    await dbClient.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TicTacToe] Cleanup failed: {ex.Message}");
                }
            }
        }
    }
    
    [System.Serializable]
    public class TicTacToeGameState
    {
        public string[] board = new string[9];
        public string currentTurn = "X";
        public string gameStatus = "waiting";
        public string playerX = "";
        public string playerO = "";
        public string winner = "";
        public DateTime lastMoveTime;
    }
    
    public enum TicTacToePlayerSymbol
    {
        None,
        X,
        O
    }
}