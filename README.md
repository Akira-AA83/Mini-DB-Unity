# Mini-DB Unity Package

**Real-time multiplayer Unity client for Mini-DB Server**

Connect your Unity games to Mini-DB Server for instant multiplayer experiences with WebSocket synchronization and server-side game logic.

## ✨ Features

- **Real-time WebSocket connection** to Mini-DB Server
- **Async/await support** with Unity's main thread dispatcher
- **Built-in game examples**: TicTacToe, Chat System, Multiplayer Pong
- **Simple SQL-like queries** from Unity C#
- **Automatic reconnection** and connection management
- **Cross-platform** (PC, Mobile, WebGL)

## 🚀 Quick Start

### 1. Installation

1. Download or clone this repository
2. Import the `Assets` folder into your Unity project
3. Ensure you have **TextMeshPro** installed (Window → TextMeshPro → Import TMP Essential Resources)

### 2. Basic Usage

```csharp
using UnityEngine;

public class GameManager : MonoBehaviour
{
    private MiniDBSQLClient dbClient;

    async void Start()
    {
        // Connect to Mini-DB Server
        dbClient = new MiniDBSQLClient("ws://localhost:8080");
        await dbClient.ConnectAsync();

        // Execute a query
        string result = await dbClient.ExecuteQueryAsync("SELECT * FROM players");
        Debug.Log($"Players: {result}");
    }

    async void OnDestroy()
    {
        if (dbClient != null)
            await dbClient.DisconnectAsync();
    }
}
```

### 3. Run Examples

The package includes complete multiplayer examples:

#### **TicTacToe Multiplayer**
- Open scene: `Assets/Scenes/TicTacToe.unity`
- Real-time 2-player TicTacToe with server-side game logic
- Demonstrates player matching and game state synchronization

#### **Chat System**
- Open scene: `Assets/Scenes/ChatDemo.unity`
- Real-time chat with multiple channels
- Shows message broadcasting and user management

#### **Multiplayer Pong**
- Open scene: `Assets/Scenes/MiniDBTestScene.unity`
- Real-time physics synchronization
- Player movement and ball physics

## 📁 Package Structure

```
Assets/
├── Runtime/                    # Core MiniDB client
│   ├── MiniDBSQLClient.cs     # Main client class
│   └── UnityMainThreadDispatcher.cs  # Thread management
├── Examples/                   # Sample games
│   ├── TicTacToe/             # Turn-based multiplayer
│   ├── ChatSystem/            # Real-time messaging
│   └── MultiplayerPong/       # Physics synchronization
└── Scenes/                    # Demo scenes
    ├── TicTacToe.unity
    ├── ChatDemo.unity
    └── MiniDBTestScene.unity
```

## 🔧 Requirements

- **Unity 2022.3 LTS** or newer
- **TextMeshPro** package
- **Mini-DB Server** running (see [Mini-DB-Server](https://github.com/Akira-AA83/Mini-DB-Server))

## 🌐 Server Connection

Make sure your Mini-DB Server is running:

```bash
# Clone and run the server
git clone https://github.com/Akira-AA83/Mini-DB-Server.git
cd Mini-DB-Server
cargo run
```

Default connection: `ws://localhost:8080`

## 📖 API Reference

### MiniDBSQLClient

```csharp
// Connection
await dbClient.ConnectAsync();
await dbClient.DisconnectAsync();

// Queries
string result = await dbClient.ExecuteQueryAsync("SELECT * FROM table");
string result = await dbClient.ExecuteQueryAsync("INSERT INTO players (name, x, y) VALUES ('Player1', 10, 20)");

// Connection status
bool isConnected = dbClient.IsConnected;
```

### Event Handling

```csharp
dbClient.OnConnected += () => Debug.Log("Connected!");
dbClient.OnDisconnected += () => Debug.Log("Disconnected!");
dbClient.OnError += (error) => Debug.LogError($"Error: {error}");
```

## 🎮 Building Multiplayer Games

### Player Management
```csharp
// Create player
await dbClient.ExecuteQueryAsync($"INSERT INTO players (id, name, x, y) VALUES ('{playerId}', '{playerName}', 0, 0)");

// Update position
await dbClient.ExecuteQueryAsync($"UPDATE players SET x = {x}, y = {y} WHERE id = '{playerId}'");

// Get all players
string players = await dbClient.ExecuteQueryAsync("SELECT * FROM players");
```

### Real-time Synchronization
```csharp
// Setup periodic sync
async void Update()
{
    if (Time.time - lastSyncTime > syncInterval)
    {
        await SyncGameState();
        lastSyncTime = Time.time;
    }
}
```

## 🔒 Security

- All game logic runs on the server via WASM modules
- Client sends intents, server validates and executes
- Built-in anti-cheat protection
- Secure WebSocket connections

## 📱 Platform Support

- ✅ **Windows/Mac/Linux** (Standalone)
- ✅ **iOS/Android** (Mobile)
- ✅ **WebGL** (Browser)

## 🤝 Contributing

1. Fork the repository
2. Create your feature branch
3. Submit a pull request

## 📄 License

MIT License - see LICENSE file for details

## 🔗 Links

- **Mini-DB Server**: https://github.com/Akira-AA83/Mini-DB-Server
- **Documentation**: [Server API Reference](https://github.com/Akira-AA83/Mini-DB-Server#api-reference)
- **Examples**: Check the `Assets/Examples/` folder

---

**Ready to build the next generation of multiplayer games with Mini-DB? Start with our examples and build something amazing!** 🚀