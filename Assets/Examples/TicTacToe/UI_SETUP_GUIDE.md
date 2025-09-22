# 🎮 TicTacToe UI Setup Guide

## 📋 Step-by-Step UI Configuration

### 1️⃣ **Create Main Canvas**
```
Hierarchy → Right-click → UI → Canvas
Rename: "TicTacToeCanvas"
```

### 2️⃣ **Create Game Manager GameObject**
```
Hierarchy → Right-click → Create Empty
Rename: "TicTacToeGameManager"
Add Component → TicTacToeGameManager script
```

### 3️⃣ **Create Game Board (3x3 Grid)**

#### Panel for Board:
```
Canvas → Right-click → UI → Panel
Rename: "GameBoard"
Position: Center of screen
Size: 300x300
```

#### Grid Layout for 3x3:
```
GameBoard → Add Component → Grid Layout Group
- Cell Size: (90, 90)
- Spacing: (10, 10)  
- Constraint: Fixed Column Count = 3
```

#### Create 9 Buttons:
```
GameBoard → Right-click → UI → Button (repeat 9 times)
Rename: "Cell_0", "Cell_1", ... "Cell_8"

For each button:
- Remove "Button" text child
- Add child: Right-click → UI → Text (TMP)
- Rename text: "CellText_0", "CellText_1", etc.
- Text settings:
  * Font Size: 36
  * Text: "" (empty)
  * Alignment: Center
  * Color: Gray
```

### 4️⃣ **Create UI Text Elements**

#### Status Text:
```
Canvas → Right-click → UI → Text (TMP)
Rename: "StatusText"
Position: Top center
Text: "Connecting..."
Font Size: 24
```

#### Player Info Text:
```
Canvas → Right-click → UI → Text (TMP)  
Rename: "PlayerInfoText"
Position: Below status
Text: ""
Font Size: 18
```

#### Turn Indicator:
```
Canvas → Right-click → UI → Text (TMP)
Rename: "TurnIndicatorText"
Position: Above board
Text: ""
Font Size: 20
Color: Yellow
```

### 5️⃣ **Create Action Buttons**

#### Join Game Button:
```
Canvas → Right-click → UI → Button
Rename: "JoinGameButton"
Position: Bottom left
Text: "Join Game"
```

#### New Game Button:
```
Canvas → Right-click → UI → Button
Rename: "NewGameButton" 
Position: Bottom center
Text: "New Game"
```

#### Leave Game Button:
```
Canvas → Right-click → UI → Button
Rename: "LeaveGameButton"
Position: Bottom right  
Text: "Leave Game"
```

### 6️⃣ **Connect to TicTacToeGameManager**

Select "TicTacToeGameManager" GameObject and in Inspector:

#### Board Buttons Array (Size: 9):
```
Element 0: Cell_0
Element 1: Cell_1
Element 2: Cell_2
Element 3: Cell_3
Element 4: Cell_4
Element 5: Cell_5
Element 6: Cell_6
Element 7: Cell_7
Element 8: Cell_8
```

#### Cell Texts Array (Size: 9):
```
Element 0: CellText_0
Element 1: CellText_1
Element 2: CellText_2
Element 3: CellText_3
Element 4: CellText_4
Element 5: CellText_5
Element 6: CellText_6
Element 7: CellText_7
Element 8: CellText_8
```

#### UI References:
```
Status Text: StatusText
Player Info Text: PlayerInfoText  
Turn Indicator Text: TurnIndicatorText
Join Game Button: JoinGameButton
New Game Button: NewGameButton
Leave Game Button: LeaveGameButton
```

#### Game Settings:
```
Player X Color: Blue (0, 0, 1, 1)
Player O Color: Red (1, 0, 0, 1)  
Neutral Color: Gray (0.5, 0.5, 0.5, 1)
Enable Debug Logs: ✅
```

## 🎨 **Layout Example**

```
┌─────────────────────────────────┐
│         StatusText              │
│       PlayerInfoText            │
│      TurnIndicatorText          │
│                                 │
│    ┌─────┬─────┬─────┐          │
│    │  0  │  1  │  2  │          │
│    ├─────┼─────┼─────┤          │
│    │  3  │  4  │  5  │          │
│    ├─────┼─────┼─────┤          │
│    │  6  │  7  │  8  │          │
│    └─────┴─────┴─────┘          │
│                                 │
│ [Join] [New Game] [Leave]       │
└─────────────────────────────────┘
```

## ⚡ **Quick Setup Tips**

1. **Use Anchors**: Set UI anchors properly for different screen sizes
2. **Font Import**: Make sure TextMeshPro is imported (Window → TextMeshPro → Import TMP Essential Resources)
3. **Canvas Scaler**: Set Canvas Scaler to "Scale With Screen Size" for responsive UI
4. **Test Layout**: Use different Game view resolutions to test UI scaling

## 🚀 **Ready to Test!**

After setup:
1. Save scene as "TicTacToeScene"
2. Make sure MiniDB server is running
3. Play scene and test "New Game" button
4. Build and run second instance to test multiplayer

---

**The board cells are numbered 0-8 from top-left to bottom-right:**
```
0 | 1 | 2
---------
3 | 4 | 5  
---------
6 | 7 | 8
```