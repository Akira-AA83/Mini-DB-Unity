using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Threading.Tasks;

namespace MiniDB.Unity.Examples.Chat
{
    /// <summary>
    /// Simple chat client interface for testing the chat system
    /// Provides additional chat features like room switching, user list, etc.
    /// </summary>
    public class ChatClient : MonoBehaviour
    {
        [Header("Chat Manager")]
        [SerializeField] private ChatManager chatManager;
        
        [Header("Additional UI")]
        [SerializeField] private TMP_InputField userNameInput;
        [SerializeField] private Button changeNameButton;
        [SerializeField] private TMP_Dropdown roomDropdown;
        [SerializeField] private Button clearChatButton;
        [SerializeField] private Toggle autoScrollToggle;
        [SerializeField] private Button reconnectButton;
        
        [Header("Settings")]
        [SerializeField] private string[] availableRooms = { "General", "Gaming", "Random", "Help" };
        
        private void Awake()
        {
            SetupUI();
        }
        
        private void Start()
        {
            if (chatManager == null)
                chatManager = FindObjectOfType<ChatManager>();
                
            if (chatManager != null)
            {
                chatManager.OnConnectionChanged += OnConnectionChanged;
                chatManager.OnNewMessage += OnNewMessage;
            }
        }
        
        private void SetupUI()
        {
            // Setup room dropdown
            if (roomDropdown != null && availableRooms.Length > 0)
            {
                roomDropdown.ClearOptions();
                roomDropdown.AddOptions(new System.Collections.Generic.List<string>(availableRooms));
                roomDropdown.onValueChanged.AddListener(OnRoomChanged);
            }
            
            // Setup buttons
            if (changeNameButton != null)
                changeNameButton.onClick.AddListener(OnChangeNameClicked);
                
            if (clearChatButton != null)
                clearChatButton.onClick.AddListener(OnClearChatClicked);
                
            if (reconnectButton != null)
                reconnectButton.onClick.AddListener(OnReconnectClicked);
                
            // Setup auto-scroll toggle
            if (autoScrollToggle != null)
                autoScrollToggle.isOn = true;
        }
        
        private void OnRoomChanged(int roomIndex)
        {
            if (roomIndex >= 0 && roomIndex < availableRooms.Length)
            {
                string newRoom = availableRooms[roomIndex];
                Debug.Log($"[ChatClient] Switching to room: {newRoom}");
                
                // For now just log - would need to implement room switching in ChatManager
                // chatManager.SwitchRoom(newRoom);
            }
        }
        
        private void OnChangeNameClicked()
        {
            if (userNameInput != null && !string.IsNullOrWhiteSpace(userNameInput.text))
            {
                string newName = userNameInput.text.Trim();
                Debug.Log($"[ChatClient] Changing name to: {newName}");
                
                // For now just log - would need to implement name changing in ChatManager
                // chatManager.ChangeUserName(newName);
                
                userNameInput.text = "";
            }
        }
        
        private void OnClearChatClicked()
        {
            if (chatManager != null)
            {
                // Clear chat history in UI (not in database)
                var messageContainer = chatManager.transform.Find("ChatUI/ScrollView/Viewport/MessageContainer");
                if (messageContainer != null)
                {
                    for (int i = messageContainer.childCount - 1; i >= 0; i--)
                    {
                        DestroyImmediate(messageContainer.GetChild(i).gameObject);
                    }
                }
                
                Debug.Log("[ChatClient] Chat cleared");
            }
        }
        
        private async void OnReconnectClicked()
        {
            if (chatManager != null)
            {
                Debug.Log("[ChatClient] Attempting to reconnect...");
                
                // Restart the chat manager
                chatManager.enabled = false;
                await Task.Delay(1000);
                chatManager.enabled = true;
            }
        }
        
        private void OnConnectionChanged(bool isConnected)
        {
            if (reconnectButton != null)
                reconnectButton.gameObject.SetActive(!isConnected);
                
            if (changeNameButton != null)
                changeNameButton.interactable = isConnected;
                
            if (roomDropdown != null)
                roomDropdown.interactable = isConnected;
        }
        
        private void OnNewMessage(ChatMessage message)
        {
            // Optional: Play sound, show notification, etc.
            if (!message.isOwnMessage)
            {
                Debug.Log($"[ChatClient] New message from {message.userName}");
                // PlayNotificationSound();
            }
        }
        
        // Utility methods for chat enhancements
        public bool SendSystemMessage(string message)
        {
            if (chatManager != null)
            {
                // Send a system/admin message
                Debug.Log($"[ChatClient] System message: {message}");
                return true;
            }
            return false;
        }
        
        public void ShowUserList()
        {
            // Show list of active users in the chat room
            Debug.Log("[ChatClient] Showing user list");
        }
        
        public void ExportChatHistory()
        {
            // Export chat history to file
            Debug.Log("[ChatClient] Exporting chat history");
        }
        
        private void OnDestroy()
        {
            if (chatManager != null)
            {
                chatManager.OnConnectionChanged -= OnConnectionChanged;
                chatManager.OnNewMessage -= OnNewMessage;
            }
        }
    }
}