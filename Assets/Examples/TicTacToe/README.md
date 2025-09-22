# 🎮 TicTacToe Multiplayer - MiniDB Unity Example

A complete **turn-based multiplayer TicTacToe** game demonstrating MiniDB real-time synchronization for strategic gameplay.

## 🎯 Features

### ⚡ Core Gameplay
- **Turn-based multiplayer** - Strategic gameplay between two players
- **Real-time move synchronization** via WebSocket notifications  
- **Automatic win/draw detection** with comprehensive game logic
- **Visual feedback** with player-specific colors and turn indicators

### 🗄️ Database Integration
- **Persistent game sessions** stored in MiniDB
- **Move history tracking** for replay and analysis
- **Real-time state synchronization** between players
- **Automatic matchmaking** system

### 🎮 Game Flow
1. **Create Game** - Player 1 creates a new game session (becomes X)
2. **Join Game** - Player 2 joins an available game (becomes O)  
3. **Turn-based Play** - Players alternate making moves
4. **Win Detection** - Automatic victory/draw detection
5. **Game End** - Results displayed and new game option

## 📋 Implementation Highlights

### 🔄 Turn-Based System
```csharp
// Clean turn management
isMyTurn = (gameState.currentTurn == "X" && playerSymbol == TicTacToePlayerSymbol.X) ||
          (gameState.currentTurn == "O" && playerSymbol == TicTacToePlayerSymbol.O);

// Immediate local updates for responsiveness  
gameState.board[cellPosition] = symbolStr;
UpdateBoardVisuals();
```

### 🏆 Win Condition Logic
```csharp
// Comprehensive win pattern checking
int[,] winPatterns = {
    {0,1,2}, {3,4,5}, {6,7,8}, // Rows
    {0,3,6}, {1,4,7}, {2,5,8}, // Columns  
    {0,4,8}, {2,4,6}           // Diagonals
};
```

### 📡 Real-time Synchronization
```csharp
// WebSocket notifications for live game updates
private void OnTableNotification(string notification)
{
    // Process only updates for current game session
    if (updatedSessionId == gameSessionId)
        _ = LoadGameState(); // Sync with database
}
```

## 🗄️ Database Schema

### Game Sessions Table (`ttt_sessions`)
```sql
CREATE TABLE ttt_sessions (
    session_id TEXT PRIMARY KEY,
    session_name TEXT NOT NULL,
    player_x TEXT DEFAULT '',
    player_o TEXT DEFAULT '',  
    current_turn TEXT DEFAULT 'X',
    board_state TEXT DEFAULT '[\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\"]',
    game_status TEXT DEFAULT 'waiting',
    winner TEXT DEFAULT '',
    created_at INTEGER DEFAULT (strftime('%s', 'now')),
    updated_at INTEGER DEFAULT (strftime('%s', 'now'))
);
```

### Move History Table (`ttt_moves`)
```sql
CREATE TABLE ttt_moves (
    move_id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id TEXT NOT NULL,
    player_id TEXT NOT NULL,
    player_symbol TEXT NOT NULL,
    cell_position INTEGER NOT NULL,
    move_timestamp INTEGER DEFAULT (strftime('%s', 'now'))
);
```

## 🎮 Controls & UI

### Game Actions
- **🎯 New Game** - Create a new game session
- **🔍 Join Game** - Find and join an available game  
- **❌ Leave Game** - Exit current game session
- **🖱️ Cell Click** - Make move (when it's your turn)

### Visual Elements
- **🟦 Blue X** - Player X moves
- **🟥 Red O** - Player O moves  
- **Turn Indicator** - Shows whose turn it is
- **Status Text** - Game state and connection info

## 🔄 Game States

| State | Description | Available Actions |
|-------|-------------|-------------------|
| `waiting` | Game created, waiting for opponent | Join Game, Leave Game |
| `playing` | Both players joined, game active | Make Move, Leave Game |
| `finished` | Game completed with winner/draw | New Game |
| `abandoned` | Player left during game | New Game |

## 📊 Comparison with Other Examples

| Feature | **TicTacToe** | MultiplayerPong | ChatSystem |
|---------|---------------|-----------------|------------|
| **Type** | Turn-based strategy | Real-time action | Real-time communication |
| **Sync Frequency** | On moves only | 60fps continuous | On messages only |
| **Complexity** | Medium | High | Low |
| **State Size** | Small (9 cells) | Medium (positions) | Variable (messages) |
| **Game Logic** | Win conditions | Physics simulation | Message persistence |

## 🎯 Learning Objectives

This example demonstrates:

1. **Turn-based multiplayer** architecture vs real-time
2. **Strategic state management** for game logic
3. **Move validation** and game rule enforcement  
4. **Matchmaking systems** with database queries
5. **Real-time notifications** for responsive gameplay
6. **Clean separation** of game logic and UI

## 🔧 Setup Instructions

1. **Add Script** - Attach `TicTacToeGameManager` to a GameObject
2. **Setup UI** - Connect buttons, texts, and board cells in Inspector
3. **Configure Database** - Ensure MiniDB server is running on localhost:8080
4. **Test Multiplayer** - Run two Unity instances (Editor + Build) to test

## 📝 Code Structure

```
TicTacToeGameManager.cs          # Main game controller
├── Game State Management        # Board, turns, players
├── Database Operations          # Sessions, moves, sync
├── UI Management               # Visual updates, interactions  
├── Multiplayer Logic           # Join, create, matchmaking
├── Win Condition Logic         # Victory/draw detection
└── Real-time Synchronization  # WebSocket notifications
```

## 🚀 Next Steps

- **🎨 Enhanced UI** - Animations and visual effects
- **👥 Spectator Mode** - Watch ongoing games
- **📊 Statistics** - Win/loss tracking per player  
- **⏰ Move Timers** - Prevent slow players
- **🏆 Tournament Mode** - Multiple game brackets

---

**🎮 Perfect for learning turn-based multiplayer game development with MiniDB!**