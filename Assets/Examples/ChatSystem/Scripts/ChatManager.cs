using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MiniDB.Unity.SQL;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace MiniDB.Unity.Examples.Chat
{
    /// <summary>
    /// Real-time chat system using MiniDB for persistence and WebSocket for real-time updates
    /// Demonstrates: Real-time communication + Database persistence + Multi-user chat
    /// </summary>
    public class ChatManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Transform messageContainer;
        [SerializeField] private TMP_InputField messageInput;
        [SerializeField] private Button sendButton;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private GameObject messagePrefab;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text userCountText;
        
        [Header("Chat Settings")]
        [SerializeField] private string chatRoomName = "General";
        private string chatRoomId = "general-room";
        [SerializeField] private int maxMessages = 50;
        [SerializeField] private bool enableDebugLogs = true;
        
        private MiniDBSQLClient dbClient;
        private string currentUserId;
        private string currentUserName;
        private List<ChatMessage> messageHistory = new List<ChatMessage>();
        private bool isConnected = false;
        private bool isSubscribed = false; // ✅ Prevent duplicate subscriptions
        private long lastPolledTimestamp = 0;
        
        // Events
        public event Action<ChatMessage> OnNewMessage;
        public event Action<bool> OnConnectionChanged;
        
        private void Awake()
        {
            // Generate TRULY unique user identity for each client instance
            currentUserId = System.Guid.NewGuid().ToString();
            currentUserName = "Player_" + UnityEngine.Random.Range(1000, 9999);
            
            if (enableDebugLogs)
                Debug.Log($"[ChatManager] 👤 Generated unique user: {currentUserName} (ID: {currentUserId})");
            
            // Setup UI events
            sendButton.onClick.AddListener(() => _ = SendMessage());
            messageInput.onSubmit.AddListener((text) => {
                if (enableDebugLogs)
                    Debug.Log($"[ChatManager] 🔍 InputField onSubmit triggered with text: '{text}' (length: {text?.Length ?? -1})");
                _ = SendMessage();
            });
        }
        
        private async void Start()
        {
            if (enableDebugLogs)
            {
                Debug.Log($"[ChatManager] 🚀 STARTING CHAT MANAGER");
                Debug.Log($"[ChatManager] - MessageContainer: {messageContainer?.name ?? "NULL"}");
                Debug.Log($"[ChatManager] - MessageInput: {messageInput?.name ?? "NULL"}");
                Debug.Log($"[ChatManager] - SendButton: {sendButton?.name ?? "NULL"}");
                Debug.Log($"[ChatManager] - ScrollRect: {scrollRect?.name ?? "NULL"}");
                Debug.Log($"[ChatManager] - StatusText: {statusText?.name ?? "NULL"}");
                Debug.Log($"[ChatManager] - UserCountText: {userCountText?.name ?? "NULL"}");
                Debug.Log($"[ChatManager] - MessagePrefab: {messagePrefab?.name ?? "NULL"}");
            }
            
            UpdateStatus("Connecting to chat server...");
            await InitializeChat();
        }
        
        private async Task InitializeChat()
        {
            try
            {
                // Create and setup MiniDB client
                var clientGO = new GameObject("ChatDBClient");
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
                    await SetupChatDatabase();
                    
                    // Check if object is still valid after async operations
                    if (this == null) return;
                    
                    await LoadMessageHistory();
                    
                    // Check again after async operations
                    if (this == null) return;
                    
                    await JoinChatRoom();
                    
                    // Final check before setting up polling
                    if (this == null) return;
                    
                    isConnected = true;
                    UpdateStatus($"Connected as {currentUserName}");
                    OnConnectionChanged?.Invoke(true);
                    
                    // Start real-time message polling
                    StartMessagePolling();
                }
                else
                {
                    if (this != null)
                        UpdateStatus("Failed to connect to chat server");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Chat initialization failed: {ex.Message}");
                if (enableDebugLogs)
                    Debug.LogError($"[ChatManager] Initialization error: {ex}");
            }
        }
        
        private async Task SetupChatDatabase()
        {
            if (enableDebugLogs)
                Debug.Log("[ChatManager] Setting up MODULAR chat database schema");
            
            try
            {
                // Use the same gaming database as other modular systems
                await dbClient.ExecuteQueryAsync("CREATE DATABASE IF NOT EXISTS gaming");
                await dbClient.ExecuteQueryAsync("USE DATABASE gaming");
                
                // Create chat rooms table (WASM modular schema)
                await dbClient.ExecuteQueryAsync(@"
                    CREATE TABLE IF NOT EXISTS chat_rooms (
                        room_id TEXT PRIMARY KEY,
                        room_name TEXT NOT NULL,
                        room_type TEXT DEFAULT 'public',
                        created_by TEXT NOT NULL,
                        max_users INTEGER DEFAULT 50,
                        created_at INTEGER DEFAULT (strftime('%s', 'now'))
                    )");
                
                // Create messages table (WASM modular schema)
                await dbClient.ExecuteQueryAsync(@"
                    CREATE TABLE IF NOT EXISTS chat_messages (
                        message_id INTEGER PRIMARY KEY AUTOINCREMENT,
                        room_id TEXT NOT NULL,
                        user_id TEXT NOT NULL,
                        username TEXT NOT NULL,
                        message_content TEXT NOT NULL,
                        message_type TEXT DEFAULT 'text',
                        timestamp INTEGER DEFAULT (strftime('%s', 'now'))
                    )");
                
                // Create participants table (WASM modular schema)
                await dbClient.ExecuteQueryAsync(@"
                    CREATE TABLE IF NOT EXISTS chat_participants (
                        participant_id INTEGER PRIMARY KEY AUTOINCREMENT,
                        room_id TEXT NOT NULL,
                        user_id TEXT NOT NULL,
                        username TEXT NOT NULL,
                        joined_at INTEGER DEFAULT (strftime('%s', 'now')),
                        role TEXT DEFAULT 'member',
                        UNIQUE(room_id, user_id)
                    )");
                
                if (enableDebugLogs)
                    Debug.Log("[ChatManager] MODULAR chat database schema ready");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatManager] Database setup failed: {ex.Message}");
                throw;
            }
        }
        
        private async Task JoinChatRoom()
        {
            try
            {
                // First ensure the room exists (WASM modular schema)
                try
                {
                    await dbClient.ExecuteQueryAsync($@"
                        INSERT INTO chat_rooms (room_id, room_name, room_type, created_by, max_users) 
                        VALUES ('{chatRoomId}', '{chatRoomName}', 'public', '{currentUserId}', 50)
                    ");
                    if (enableDebugLogs)
                        Debug.Log($"[ChatManager] Created new room: {chatRoomName}");
                }
                catch
                {
                    if (enableDebugLogs)
                        Debug.Log($"[ChatManager] Room already exists: {chatRoomName}");
                }
                
                // Add user to participants (WASM modular schema)
                try
                {
                    await dbClient.ExecuteQueryAsync($@"
                        INSERT INTO chat_participants (room_id, user_id, username, role) 
                        VALUES ('{chatRoomId}', '{currentUserId}', '{currentUserName}', 'member')
                    ");
                }
                catch
                {
                    // User already in room, update timestamp
                    await dbClient.ExecuteQueryAsync($@"
                        UPDATE chat_participants 
                        SET username = '{currentUserName}', joined_at = strftime('%s', 'now')
                        WHERE room_id = '{chatRoomId}' AND user_id = '{currentUserId}'
                    ");
                }
                
                if (enableDebugLogs)
                    Debug.Log($"[ChatManager] Joined MODULAR chat room: {chatRoomName} (ID: {chatRoomId})");
                
                await UpdateUserCount();
                
                // NOW subscribe to real-time chat messages and participants (after database is set)
                await SubscribeToMessages();
                await SubscribeToParticipants();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatManager] Failed to join chat room: {ex.Message}");
            }
        }
        
        private async Task LoadMessageHistory()
        {
            try
            {
                string response = await dbClient.ExecuteQueryAsync($@"
                    SELECT username, message_content, timestamp 
                    FROM chat_messages 
                    WHERE room_id = '{chatRoomId}' 
                    ORDER BY timestamp ASC 
                    LIMIT {maxMessages}
                ");
                
                var jsonResponse = JObject.Parse(response);
                if (jsonResponse["results"] is JArray results)
                {
                    foreach (var result in results)
                    {
                        var message = new ChatMessage
                        {
                            userName = result["username"]?.ToString() ?? "Unknown",
                            messageText = result["message_content"]?.ToString() ?? "",
                            timestamp = ParseTimestamp(result["timestamp"]?.ToString()),
                            isOwnMessage = result["username"]?.ToString() == currentUserName
                        };
                        
                        AddMessageToUI(message);
                        messageHistory.Add(message);
                    }
                }
                
                if (enableDebugLogs)
                    Debug.Log($"[ChatManager] Loaded {messageHistory.Count} message(s) from MODULAR history");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatManager] Failed to load message history: {ex.Message}");
            }
        }
        
        public async Task<bool> SendMessage()
        {
            if (enableDebugLogs)
            {
                Debug.Log($"[ChatManager] 🔍 SendMessage() called");
                Debug.Log($"[ChatManager] - isConnected: {isConnected}");
                Debug.Log($"[ChatManager] - messageInput.text: '{messageInput.text}' (length: {messageInput.text?.Length ?? -1})");
                Debug.Log($"[ChatManager] - IsNullOrWhiteSpace: {string.IsNullOrWhiteSpace(messageInput.text)}");
            }
            
            if (!isConnected || string.IsNullOrWhiteSpace(messageInput.text))
            {
                if (enableDebugLogs)
                    Debug.Log($"[ChatManager] ⏹️ SendMessage() early return - not connected or empty text");
                return false;
            }
            
            string messageText = messageInput.text.Trim();
            messageInput.text = "";
            
            try
            {
                // Insert message into database with WASM modular schema
                long unixTimestamp = ((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds();
                
                await dbClient.ExecuteQueryAsync($@"
                    INSERT INTO chat_messages (room_id, user_id, username, message_content, message_type, timestamp) 
                    VALUES ('{chatRoomId}', '{currentUserId}', '{currentUserName}', '{EscapeSqlString(messageText)}', 'text', {unixTimestamp})
                ");
                
                // Create message object with same timestamp
                var newMessage = new ChatMessage
                {
                    userName = currentUserName,
                    messageText = messageText,
                    timestamp = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).DateTime,
                    isOwnMessage = true
                };
                
                AddMessageToUI(newMessage);
                messageHistory.Add(newMessage);
                OnNewMessage?.Invoke(newMessage);
                
                if (enableDebugLogs)
                    Debug.Log($"[ChatManager] 📤 Message sent: '{messageText}' with timestamp {unixTimestamp} ({newMessage.timestamp})");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatManager] Failed to send message: {ex.Message}");
                UpdateStatus("Failed to send message");
                return false;
            }
        }
        
        private async void StartMessagePolling()
        {
            // Check if object is still valid before starting polling
            if (this == null || !isConnected)
                return;
                
            // ✅ CRITICAL FIX: Actually subscribe to the chat_messages table!
            await SubscribeToMessages();
            
            // Real-time subscriptions setup complete
            if (enableDebugLogs)
                Debug.Log("[ChatManager] 🚀 Real-time chat system initialized with WebSocket subscriptions");
        }
        
        /// <summary>
        /// Subscribe to real-time chat_messages table notifications
        /// </summary>
        private async Task SubscribeToMessages()
        {
            // ✅ CRITICAL FIX: Prevent duplicate subscriptions
            if (isSubscribed)
            {
                if (enableDebugLogs)
                    Debug.Log("[ChatManager] 🔄 Already subscribed to chat_messages, skipping...");
                return;
            }
            
            try
            {
                if (enableDebugLogs)
                    Debug.Log("[ChatManager] 📡 Subscribing to chat_messages table...");
                
                bool subscribed = await dbClient.SubscribeToTable("chat_messages");
                
                if (subscribed)
                {
                    isSubscribed = true; // ✅ Mark as subscribed
                    if (enableDebugLogs)
                        Debug.Log("[ChatManager] ✅ Successfully subscribed to chat_messages real-time updates");
                }
                else
                {
                    if (enableDebugLogs)
                        Debug.LogError("[ChatManager] ❌ Failed to subscribe to chat_messages! Real-time updates disabled.");
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogError($"[ChatManager] ❌ Subscription error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Subscribe to real-time chat_participants table notifications for user count updates
        /// </summary>
        private async Task SubscribeToParticipants()
        {
            try
            {
                if (enableDebugLogs)
                    Debug.Log("[ChatManager] 📡 Subscribing to chat_participants table...");
                
                bool subscribed = await dbClient.SubscribeToTable("chat_participants");
                
                if (subscribed)
                {
                    if (enableDebugLogs)
                        Debug.Log("[ChatManager] ✅ Successfully subscribed to chat_participants real-time updates");
                }
                else
                {
                    if (enableDebugLogs)
                        Debug.LogError("[ChatManager] ❌ Failed to subscribe to chat_participants! User count updates disabled.");
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogError($"[ChatManager] ❌ Participants subscription error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle real-time table notifications from WebSocket
        /// </summary>
        private void OnTableNotification(string notification)
        {
            try
            {
                if (enableDebugLogs)
                    Debug.Log($"[ChatManager] 📨 Received table notification: {notification}");
                
                // Parse notification JSON to extract new message data
                var jsonNotification = JObject.Parse(notification);
                
                // Check if this is a chat_messages notification
                if (jsonNotification["table"]?.ToString() == "chat_messages")
                {
                    var dataField = jsonNotification["data"];
                    if (dataField != null)
                    {
                        // ✅ CRITICAL FIX: Parse the data field as JSON string
                        JObject messageData;
                        if (dataField.Type == JTokenType.String)
                        {
                            // Data is a JSON string, parse it
                            messageData = JObject.Parse(dataField.ToString());
                        }
                        else
                        {
                            // Data is already an object
                            messageData = (JObject)dataField;
                        }
                        var userName = messageData["username"]?.ToString() ?? "Unknown";
                        var messageText = messageData["message_content"]?.ToString() ?? "";
                        var userId = messageData["user_id"]?.ToString() ?? "";
                        var timestampStr = messageData["timestamp"]?.ToString();
                        
                        // Skip our own messages
                        if (userId == currentUserId)
                        {
                            if (enableDebugLogs)
                                Debug.Log("[ChatManager] 🔄 Skipping own message from notification");
                            return;
                        }
                        
                        if (enableDebugLogs)
                            Debug.Log($"[ChatManager] 🎉 New real-time message: '{messageText}' by {userName}");
                        
                        var timestamp = ParseTimestamp(timestampStr);
                        
                        var message = new ChatMessage
                        {
                            userName = userName,
                            messageText = messageText,
                            timestamp = timestamp,
                            isOwnMessage = false
                        };
                        
                        // Add to UI on main thread
                        AddMessageToUI(message);
                        messageHistory.Add(message);
                        OnNewMessage?.Invoke(message);
                    }
                }
                // Check if this is a chat_participants notification (for user count updates)
                else if (jsonNotification["table"]?.ToString() == "chat_participants")
                {
                    if (enableDebugLogs)
                        Debug.Log("[ChatManager] 👥 Received chat_participants notification - updating user count");
                    
                    // Update user count when participants change
                    _ = UpdateUserCount();
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogError($"[ChatManager] ❌ Error processing table notification: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Polling-based message checking (FALLBACK ONLY). Real-time flow:
        /// 1. Client A sends message → Server saves to database
        /// 2. Client B polls every 0.5s → SELECT new messages
        /// 3. If found, Client B displays them
        /// 
        /// TODO: Implement WebSocket broadcasting for true real-time:
        /// 1. Client A sends message → Server saves + broadcasts to all clients
        /// 2. All clients receive instant notification
        /// </summary>
        private async void CheckForNewMessages()
        {
            if (!isConnected || this == null || dbClient == null) return;
            
            try
            {
                // Use dedicated polling timestamp for more reliable real-time updates
                long currentTimestamp = ((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds();
                
                // Initialize lastPolledTimestamp if this is the first poll
                if (lastPolledTimestamp == 0)
                {
                    // Start polling from 1 minute ago to catch recent messages
                    lastPolledTimestamp = currentTimestamp - 60;
                    if (enableDebugLogs)
                        Debug.Log($"[ChatManager] 🔍 Initializing polling timestamp: {lastPolledTimestamp}");
                }
                
                long lastTimestamp = lastPolledTimestamp;
                
                if (enableDebugLogs)
                {
                    Debug.Log($"[ChatManager] 🔍 POLLING DEBUG - Current user: {currentUserName} (ID: {currentUserId})");
                    Debug.Log($"[ChatManager] 🔍 Message history count: {messageHistory.Count}");
                    Debug.Log($"[ChatManager] 🔍 Polling timestamp: {lastTimestamp}");
                    Debug.Log($"[ChatManager] 🔍 Current Unix time: {currentTimestamp}");
                }
                
                string sqlQuery = $@"
                    SELECT username, message_content, timestamp 
                    FROM chat_messages 
                    WHERE room_id = '{chatRoomId}' AND timestamp > {lastTimestamp} AND user_id != '{currentUserId}'
                    ORDER BY timestamp ASC
                ";
                
                if (enableDebugLogs)
                    Debug.Log($"[ChatManager] 🔍 Executing polling query: {sqlQuery}");
                
                string response = await dbClient.ExecuteQueryAsync(sqlQuery);
                
                if (enableDebugLogs)
                    Debug.Log($"[ChatManager] 🔍 Polling response: {response}");
                
                var jsonResponse = JObject.Parse(response);
                var results = jsonResponse["results"] as JArray;
                bool foundMessages = results != null && results.Count > 0;
                
                if (foundMessages)
                {
                    if (enableDebugLogs)
                        Debug.Log($"[ChatManager] 🔍 Found {results.Count} new messages from other users!");
                    
                    foreach (var result in results)
                    {
                        var messageText = result["message_content"]?.ToString() ?? "";
                        var userName = result["username"]?.ToString() ?? "Unknown";
                        var timestampStr = result["timestamp"]?.ToString();
                        var timestamp = ParseTimestamp(timestampStr);
                        
                        if (enableDebugLogs)
                            Debug.Log($"[ChatManager] 🔍 Processing message: '{messageText}' by {userName} at timestamp {timestampStr} -> {timestamp}");
                        
                        var message = new ChatMessage
                        {
                            userName = userName,
                            messageText = messageText,
                            timestamp = timestamp,
                            isOwnMessage = false
                        };
                        
                        AddMessageToUI(message);
                        messageHistory.Add(message);
                        OnNewMessage?.Invoke(message);
                    }
                    
                    await UpdateUserCount();
                    
                    // Update to current time when we found messages
                    lastPolledTimestamp = currentTimestamp;
                    if (enableDebugLogs)
                        Debug.Log($"[ChatManager] 🔍 Found messages - updated polling timestamp to: {lastPolledTimestamp}");
                }
                else
                {
                    if (enableDebugLogs)
                        Debug.Log($"[ChatManager] 🔍 No new messages found in polling query");
                    
                    // Move timestamp forward more slowly to avoid missing messages
                    lastPolledTimestamp = currentTimestamp - 5; // Keep a 5-second buffer for faster polling
                    if (enableDebugLogs)
                        Debug.Log($"[ChatManager] 🔍 No messages - kept polling timestamp with buffer: {lastPolledTimestamp}");
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogWarning($"[ChatManager] Message polling error: {ex.Message}");
            }
        }
        
        private void AddMessageToUI(ChatMessage message)
        {
            if (messageContainer == null)
            {
                if (enableDebugLogs)
                    Debug.LogWarning("[ChatManager] MessageContainer is null, cannot display message");
                return;
            }
            
            if (enableDebugLogs)
                Debug.Log($"[ChatManager] Adding message to UI: '{message.messageText}' by {message.userName}");
            
            // Try multiple approaches to ensure message visibility
            CreateHighVisibilityMessage(message);
            
            // ALSO create a super simple fallback message
            CreateUltraSimpleMessage(message);
            
            // Limit message count
            if (messageContainer.childCount > maxMessages)
            {
                DestroyImmediate(messageContainer.GetChild(0).gameObject);
            }
            
            // Force scroll to show new message with extensive debugging
            if (scrollRect != null)
            {
                Debug.Log($"[ChatManager] ======= SCROLL DEBUGGING =======");
                Debug.Log($"[ChatManager] ScrollRect: {scrollRect.name}");
                Debug.Log($"[ChatManager] ScrollRect content: {scrollRect.content?.name ?? "NULL"}");
                Debug.Log($"[ChatManager] ScrollRect viewport: {scrollRect.viewport?.name ?? "NULL"}");
                Debug.Log($"[ChatManager] Content child count: {scrollRect.content?.childCount ?? -1}");
                Debug.Log($"[ChatManager] MessageContainer child count: {messageContainer.childCount}");
                Debug.Log($"[ChatManager] ScrollRect vertical: {scrollRect.vertical}");
                Debug.Log($"[ChatManager] ScrollRect position before: {scrollRect.verticalNormalizedPosition}");
                
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
                
                Debug.Log($"[ChatManager] ScrollRect position after: {scrollRect.verticalNormalizedPosition}");
                Debug.Log($"[ChatManager] ======= SCROLL DEBUG COMPLETE =======");
            }
            else
            {
                Debug.LogError("[ChatManager] ❌ ScrollRect is NULL - cannot scroll to new message!");
            }
        }
        
        private void CreateHighVisibilityMessage(ChatMessage message)
        {
            Debug.Log($"[ChatManager] ======= STARTING MESSAGE CREATION =======");
            Debug.Log($"[ChatManager] Message text: '{message.messageText}'");
            Debug.Log($"[ChatManager] MessageContainer null check: {messageContainer == null}");
            
            if (messageContainer == null)
            {
                Debug.LogError("[ChatManager] ❌ CRITICAL: MessageContainer is NULL!");
                return;
            }
            
            // Create container GameObject with background
            GameObject msgContainer = new GameObject($"ChatMessage_{DateTime.Now.Ticks}");
            Debug.Log($"[ChatManager] Created container: {msgContainer.name}");
            
            msgContainer.transform.SetParent(messageContainer, false);
            Debug.Log($"[ChatManager] Set parent to: {messageContainer.name}");
            
            // Add RectTransform to container
            var containerRect = msgContainer.AddComponent<RectTransform>();
            containerRect.sizeDelta = new Vector2(400f, 80f); // Fixed width, tall message
            containerRect.anchorMin = new Vector2(0, 1); // Top-left anchor
            containerRect.anchorMax = new Vector2(1, 1); // Top-right anchor
            containerRect.pivot = new Vector2(0.5f, 1f); // Top center pivot
            
            Debug.Log($"[ChatManager] Container RectTransform configured");
            Debug.Log($"[ChatManager] - Size: {containerRect.sizeDelta}");
            Debug.Log($"[ChatManager] - Anchors: {containerRect.anchorMin} to {containerRect.anchorMax}");
            
            // Add BRIGHT colored background to container
            var image = msgContainer.AddComponent<UnityEngine.UI.Image>();
            image.color = message.isOwnMessage ? Color.cyan : Color.yellow; // VERY VISIBLE COLORS
            Debug.Log($"[ChatManager] Background color set to: {image.color}");
            
            // Add LayoutElement to container
            var layoutElement = msgContainer.AddComponent<UnityEngine.UI.LayoutElement>();
            layoutElement.preferredHeight = 80f;
            layoutElement.flexibleWidth = 1f;
            layoutElement.minHeight = 80f;
            Debug.Log($"[ChatManager] LayoutElement configured - height: {layoutElement.preferredHeight}");
            
            // Create separate child GameObject for text
            GameObject textObject = new GameObject("MessageText");
            textObject.transform.SetParent(msgContainer.transform, false);
            Debug.Log($"[ChatManager] Text object created and parented");
            
            var textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 10); // Padding
            textRect.offsetMax = new Vector2(-10, -10);
            Debug.Log($"[ChatManager] Text RectTransform configured");
            
            // Add text component to separate GameObject
            var text = textObject.AddComponent<UnityEngine.UI.Text>();
            text.text = $"★★★ [{message.timestamp:HH:mm}] {message.userName}: {message.messageText} ★★★";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 24; // Even larger font
            text.color = Color.black; // Black text on bright background
            text.alignment = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            
            Debug.Log($"[ChatManager] Text component configured:");
            Debug.Log($"[ChatManager] - Text: '{text.text}'");
            Debug.Log($"[ChatManager] - Font: {text.font?.name ?? "NULL"}");
            Debug.Log($"[ChatManager] - Color: {text.color}");
            Debug.Log($"[ChatManager] - Size: {text.fontSize}");
            
            // Force immediate Canvas update
            Canvas.ForceUpdateCanvases();
            Debug.Log($"[ChatManager] Forced canvas update");
            
            // Check hierarchy after creation
            Debug.Log($"[ChatManager] ======= POST-CREATION STATUS =======");
            Debug.Log($"[ChatManager] - Container active in hierarchy: {msgContainer.activeInHierarchy}");
            Debug.Log($"[ChatManager] - Text active in hierarchy: {textObject.activeInHierarchy}");
            Debug.Log($"[ChatManager] - Container world position: {msgContainer.transform.position}");
            Debug.Log($"[ChatManager] - Container local position: {msgContainer.transform.localPosition}");
            Debug.Log($"[ChatManager] - Container rect position: {containerRect.anchoredPosition}");
            Debug.Log($"[ChatManager] - Parent child count: {messageContainer.childCount}");
            Debug.Log($"[ChatManager] - Parent active: {messageContainer.gameObject.activeInHierarchy}");
            Debug.Log($"[ChatManager] - Parent Canvas: {messageContainer.GetComponentInParent<Canvas>()?.name ?? "NULL"}");
            Debug.Log($"[ChatManager] - ScrollRect content: {scrollRect?.content?.name ?? "NULL"}");
            
            // Additional Canvas/UI debugging
            var canvas = messageContainer.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                Debug.Log($"[ChatManager] - Canvas render mode: {canvas.renderMode}");
                Debug.Log($"[ChatManager] - Canvas scaler: {canvas.GetComponent<UnityEngine.UI.CanvasScaler>()?.uiScaleMode ?? UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize}");
                Debug.Log($"[ChatManager] - Canvas active: {canvas.gameObject.activeInHierarchy}");
            }
            
            Debug.Log($"[ChatManager] ======= MESSAGE CREATION COMPLETE =======");
        }
        
        private void CreateUltraSimpleMessage(ChatMessage message)
        {
            Debug.Log($"[ChatManager] >>>>>>> CREATING ULTRA SIMPLE MESSAGE (FIXED) <<<<<<<");
            
            // Create background container
            GameObject bgContainer = new GameObject($"UltraSimpleBG_{DateTime.Now.Ticks}");
            bgContainer.transform.SetParent(messageContainer, false);
            
            var bgRect = bgContainer.AddComponent<RectTransform>();
            bgRect.sizeDelta = new Vector2(500f, 100f);
            bgRect.anchoredPosition = new Vector2(0, -messageContainer.childCount * 50f);
            
            var bg = bgContainer.AddComponent<UnityEngine.UI.Image>();
            bg.color = Color.red; // BRIGHT RED - impossible to miss
            
            // Create separate text child
            GameObject textObj = new GameObject("UltraText");
            textObj.transform.SetParent(bgContainer.transform, false);
            
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = Vector2.zero;
            
            var txt = textObj.AddComponent<UnityEngine.UI.Text>();
            txt.text = $"🔴 ULTRA SIMPLE: {message.messageText} 🔴";
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 20;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.fontStyle = FontStyle.Bold;
            
            Debug.Log($"[ChatManager] Ultra simple message (FIXED) created:");
            Debug.Log($"[ChatManager] - BG Name: {bgContainer.name}");
            Debug.Log($"[ChatManager] - Text Name: {textObj.name}");
            Debug.Log($"[ChatManager] - BG Position: {bgRect.anchoredPosition}");
            Debug.Log($"[ChatManager] - BG Size: {bgRect.sizeDelta}");
            Debug.Log($"[ChatManager] - BG Active: {bgContainer.activeInHierarchy}");
            Debug.Log($"[ChatManager] - Text Active: {textObj.activeInHierarchy}");
            Debug.Log($"[ChatManager] >>>>>>> ULTRA SIMPLE (FIXED) COMPLETE <<<<<<<");
        }
        
        private void CreateSimpleTextMessage(ChatMessage message)
        {
            // Create container with background
            GameObject messageContainer = new GameObject($"Message_{message.userName}_{DateTime.Now.Ticks}");
            messageContainer.transform.SetParent(this.messageContainer, false);
            
            var containerRect = messageContainer.AddComponent<RectTransform>();
            containerRect.sizeDelta = new Vector2(0, 50f);
            containerRect.anchorMin = new Vector2(0, 1);
            containerRect.anchorMax = new Vector2(1, 1);
            containerRect.pivot = new Vector2(0.5f, 1);
            
            // Add background image to container with visible color
            var background = messageContainer.AddComponent<UnityEngine.UI.Image>();
            background.color = message.isOwnMessage ? 
                new Color(0.2f, 0.6f, 1f, 0.8f) : // More opaque blue for own messages
                new Color(0.9f, 0.9f, 0.9f, 0.8f); // More opaque gray for others
            
            // Add LayoutElement to container
            var layoutElement = messageContainer.AddComponent<UnityEngine.UI.LayoutElement>();
            layoutElement.preferredHeight = 50f;
            layoutElement.flexibleWidth = 1f;
            layoutElement.minHeight = 50f;
            
            // Create separate child GameObject for text
            GameObject textObject = new GameObject("MessageText");
            textObject.transform.SetParent(messageContainer.transform, false);
            
            var textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 5);  // Padding
            textRect.offsetMax = new Vector2(-10, -5);
            
            var textComp = textObject.AddComponent<UnityEngine.UI.Text>();
            textComp.text = $"[{message.timestamp:HH:mm}] {message.userName}: {message.messageText}";
            textComp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textComp.fontSize = 18; // Larger font
            textComp.color = message.isOwnMessage ? Color.white : Color.black;
            textComp.alignment = TextAnchor.MiddleLeft;
            textComp.fontStyle = FontStyle.Bold; // Bold text for better visibility
            textComp.verticalOverflow = VerticalWrapMode.Overflow;
            textComp.horizontalOverflow = HorizontalWrapMode.Wrap;
            
            if (enableDebugLogs)
            {
                Debug.Log($"[ChatManager] Created message container: {messageContainer.name}");
                Debug.Log($"[ChatManager] - Container position: {containerRect.anchoredPosition}");
                Debug.Log($"[ChatManager] - Container size: {containerRect.sizeDelta}");
                Debug.Log($"[ChatManager] - Parent container child count: {this.messageContainer.childCount}");
                Debug.Log($"[ChatManager] - Message text: '{message.messageText}'");
                Debug.Log($"[ChatManager] - Background color: {background.color}");
                Debug.Log($"[ChatManager] - Text color: {textComp.color}");
            }
        }
        
        private async Task UpdateUserCount()
        {
            try
            {
                string response = await dbClient.ExecuteQueryAsync($@"
                    SELECT COUNT(*) as user_count 
                    FROM chat_participants 
                    WHERE room_id = '{chatRoomId}'
                ");
                
                var jsonResponse = JObject.Parse(response);
                if (jsonResponse["results"] is JArray results && results.Count > 0)
                {
                    int userCount = int.Parse(results[0]["COUNT"]?.ToString() ?? "0");
                    if (userCountText != null)
                        userCountText.text = $"Users Online: {userCount}";
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogWarning($"[ChatManager] Failed to update user count: {ex.Message}");
            }
        }
        
        private void UpdateStatus(string status)
        {
            if (this == null) return;
            
            if (statusText != null)
                statusText.text = status;
            
            if (enableDebugLogs)
                Debug.Log($"[ChatManager] Status: {status}");
        }
        
        private DateTime ParseTimestamp(string timestampStr)
        {
            if (long.TryParse(timestampStr, out long timestamp))
                return DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
            return DateTime.Now;
        }
        
        private string EscapeSqlString(string input)
        {
            return input.Replace("'", "''");
        }
        
        private async void OnDatabaseConnected()
        {
            if (enableDebugLogs)
                Debug.Log("[ChatManager] Database connected");
        }
        
        private async void OnDatabaseDisconnected(string reason)
        {
            isConnected = false;
            isSubscribed = false; // ✅ Reset subscription flag on disconnect
            UpdateStatus($"Disconnected: {reason}");
            OnConnectionChanged?.Invoke(false);
            
            // Unsubscribe from table notifications
            try
            {
                if (dbClient != null)
                {
                    await dbClient.UnsubscribeFromTable("chat_messages");
                }
            }
            catch (Exception ex)
            {
                if (enableDebugLogs)
                    Debug.LogWarning($"[ChatManager] Failed to unsubscribe: {ex.Message}");
            }
            
            if (enableDebugLogs)
                Debug.Log($"[ChatManager] Database disconnected: {reason}");
        }
        
        private void OnDatabaseError(string error)
        {
            UpdateStatus($"Error: {error}");
            
            if (enableDebugLogs)
                Debug.LogError($"[ChatManager] Database error: {error}");
        }
        
        private async void OnDestroy()
        {
            // Leave chat room
            if (isConnected && dbClient != null)
            {
                try
                {
                    await dbClient.ExecuteQueryAsync($@"
                        DELETE FROM chat_participants 
                        WHERE user_id = '{currentUserId}' AND room_id = '{chatRoomId}'
                    ");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ChatManager] Failed to update offline status: {ex.Message}");
                }
                
                await dbClient.DisconnectAsync();
            }
        }
    }
    
    [System.Serializable]
    public class ChatMessage
    {
        public string userName;
        public string messageText;
        public DateTime timestamp;
        public bool isOwnMessage;
    }
}