using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

namespace MiniDB.Unity.Examples.Chat
{
    /// <summary>
    /// UI component for displaying individual chat messages
    /// Handles styling for own vs other messages, timestamps, and user names
    /// </summary>
    public class MessageUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text userNameText;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private TMP_Text timestampText;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private LayoutElement layoutElement;
        
        [Header("Styling")]
        [SerializeField] private Color ownMessageColor = new Color(0.2f, 0.6f, 1f, 0.3f);
        [SerializeField] private Color otherMessageColor = new Color(0.8f, 0.8f, 0.8f, 0.3f);
        [SerializeField] private Color ownTextColor = Color.white;
        [SerializeField] private Color otherTextColor = Color.black;
        
        private ChatMessage currentMessage;
        
        public void SetMessage(ChatMessage message)
        {
            Debug.Log($"[MessageUI] SetMessage called with: {message?.messageText ?? "null message"}");
            currentMessage = message;
            UpdateUI();
        }
        
        private void UpdateUI()
        {
            if (currentMessage == null) 
            {
                Debug.LogWarning("[MessageUI] UpdateUI called but currentMessage is null");
                return;
            }
            
            Debug.Log($"[MessageUI] UpdateUI - Setting up message: {currentMessage.messageText}");
            
            // Set message content
            if (messageText != null)
            {
                messageText.text = currentMessage.messageText;
                messageText.color = currentMessage.isOwnMessage ? ownTextColor : otherTextColor;
            }
            
            // Set user name
            if (userNameText != null)
            {
                userNameText.text = currentMessage.userName;
                userNameText.color = currentMessage.isOwnMessage ? ownTextColor : otherTextColor;
                
                // Hide username for own messages or make it smaller
                userNameText.fontSize = currentMessage.isOwnMessage ? 10f : 12f;
                userNameText.fontStyle = currentMessage.isOwnMessage ? FontStyles.Italic : FontStyles.Bold;
            }
            
            // Set timestamp
            if (timestampText != null)
            {
                timestampText.text = FormatTimestamp(currentMessage.timestamp);
                timestampText.color = currentMessage.isOwnMessage ? 
                    new Color(ownTextColor.r, ownTextColor.g, ownTextColor.b, 0.7f) : 
                    new Color(otherTextColor.r, otherTextColor.g, otherTextColor.b, 0.7f);
            }
            
            // Set background styling
            if (backgroundImage != null)
            {
                backgroundImage.color = currentMessage.isOwnMessage ? ownMessageColor : otherMessageColor;
                
                // Align message bubble
                var rectTransform = backgroundImage.rectTransform;
                var anchorMin = currentMessage.isOwnMessage ? new Vector2(0.2f, 0f) : new Vector2(0f, 0f);
                var anchorMax = currentMessage.isOwnMessage ? new Vector2(1f, 1f) : new Vector2(0.8f, 1f);
                
                rectTransform.anchorMin = anchorMin;
                rectTransform.anchorMax = anchorMax;
                rectTransform.offsetMin = Vector2.zero;
                rectTransform.offsetMax = Vector2.zero;
            }
            
            // Adjust layout if needed
            if (layoutElement != null)
            {
                layoutElement.preferredHeight = CalculateMessageHeight();
            }
        }
        
        private string FormatTimestamp(DateTime timestamp)
        {
            var now = DateTime.Now;
            var timeDiff = now - timestamp;
            
            if (timeDiff.TotalMinutes < 1)
                return "now";
            else if (timeDiff.TotalHours < 1)
                return $"{(int)timeDiff.TotalMinutes}m ago";
            else if (timeDiff.TotalDays < 1)
                return timestamp.ToString("HH:mm");
            else
                return timestamp.ToString("MM/dd HH:mm");
        }
        
        private float CalculateMessageHeight()
        {
            float baseHeight = 60f; // Base height for single line
            
            if (messageText != null && currentMessage != null)
            {
                // Use TextMeshPro's built-in preferred height calculation
                messageText.text = currentMessage.messageText;
                
                // Force update to calculate proper size
                messageText.ForceMeshUpdate();
                
                // Get the preferred height from TextMeshPro
                float textHeight = messageText.preferredHeight;
                
                return Mathf.Max(baseHeight, textHeight + 40f); // Add padding
            }
            
            return baseHeight;
        }
        
        public void OnMessageClicked()
        {
            // Optional: Add click interaction (e.g., show full timestamp, copy message)
            Debug.Log($"[MessageUI] Clicked message from {currentMessage.userName}: {currentMessage.messageText}");
        }
    }
}