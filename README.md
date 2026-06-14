# LorAI MCP

**Library of Ruina AI Agent** — 让 AI 通过 MCP 协议直接操控《废墟图书馆》。

An MCP (Model Context Protocol) server + in-game C# mod that lets AI agents play *Library of Ruina* — reading game state, navigating UI, fighting battles, and selecting emotion cards.

---

## 架构

```
┌──────────────┐     MCP (stdio)      ┌──────────────┐     HTTP      ┌──────────────┐
│   AI Agent   │ <──────────────────> │  Python MCP  │ <──────────> │   C# Mod     │
│  (LLM brain) │     tools call       │  (lor_mcp)   │  localhost    │  (LorAIHost) │
└──────────────┘                      └──────────────┘   :17127     └──────────────┘
```

**设计原则：LLM 做决策，工具做执行。**

- AI 读取游戏状态（关卡、血量、手牌、敌人）
- AI 决定打哪个关卡、选哪张情绪卡
- 工具负责执行：导航 UI、出牌、选择情绪卡、推进 phase

---

## 快速开始

### 前置条件

| 依赖 | 版本 |
|---|---|
| Steam | 安装 *Library of Ruina* (1.1.0.6a13) |
| Python | 3.10+ |
| .NET SDK | 4.7.2 兼容（Visual Studio Build Tools 或 .NET SDK） |
| [BaseMod](https://github.com/USay560828/LoRBaseMod) | 游戏内 Mod 加载器 |

### Step 1: 克隆仓库

```bash
git clone https://github.com/<your-username>/lorAI-mcp.git
cd lorAI-mcp
pip install -e .
```

### Step 2: 编译并部署 C# Mod

```bash
cd LorAIHost
dotnet build -c Release
```

将编译产物部署到游戏目录：

```bash
# Windows (Git Bash)
GAME_DIR="/c/Program Files (x86)/Steam/steamapps/common/Library Of Ruina"
mkdir -p "$GAME_DIR/LibraryOfRuina_Data/Mods/LorAIHost/Assemblies"
cp bin/Release/LorAIHost.dll "$GAME_DIR/LibraryOfRuina_Data/Mods/LorAIHost/Assemblies/"
cp StageModInfo.xml "$GAME_DIR/LibraryOfRuina_Data/Mods/LorAIHost/"
```

> 修改 `LorAIHost/LorAIHost.csproj` 中的 `<GameDir>` 路径，指向你的游戏安装位置。

启动游戏，在 Mod 管理器中启用 **LorAIHost**，然后重启游戏。

### Step 3: 配置 MCP

#### Agent 一键配置

如果你在使用 kimi-code（或任何兼容 `.kimi-code/mcp.json` 的客户端），直接运行：

```bash
python scripts/setup-mcp.py
```

脚本会自动把 `lor-mcp` 注册到 `~/.kimi-code/mcp.json`，重启客户端即可使用。

#### 手动配置

在你的 MCP 客户端配置文件中添加：

```json
{
  "mcpServers": {
    "lor-mcp": {
      "command": "lor-mcp",
      "args": [],
      "env": {
        "LOR_API_BASE_URL": "http://localhost:17127"
      }
    }
  }
}
```

如果 `lor-mcp` 不在 PATH 上，可以用 `python -m lor_mcp.server` 替代。

### 验证

1. 从 Steam 启动游戏，等待标题画面
2. 测试 bridge 连接：

```bash
curl http://localhost:17127/health
# {"status":"ok","version":"1.0.0"}
```

3. 在 AI Agent 中调用 `health_check` 工具确认 MCP 连通

---

## 项目结构

```
lorAI-mcp/
├── lor_mcp/                   # Python MCP Server
│   ├── server.py              # FastMCP 入口 + 基础 tools
│   ├── tools.py               # Guided tools（战斗自动化）
│   ├── config.py              # 配置
│   └── state.py               # 状态模型
├── LorAIHost/                 # C# Mod（HTTP bridge + 状态导出 + 战斗自动化）
│   ├── LorAIHostMod.cs        # ModInitializer 入口
│   ├── HttpServer.cs          # HTTP 服务（端口 17127）
│   ├── StateExporter.cs       # 游戏状态导出
│   ├── ActionHandler.cs       # Action 分发
│   ├── AdvancedActions.cs     # 选书/情绪卡/phase 推进/战斗单位导出
│   ├── BattleActions.cs       # 出牌/确认/结束战斗
│   ├── NavigationActions.cs   # UI 导航
│   ├── StoryActions.cs        # 剧情跳过
│   ├── UtilityActions.cs      # 反射调用/诊断
│   ├── UpdateHook.cs          # 主线程队列
│   ├── ReflectionHelper.cs    # 反射工具库
│   ├── JsonHelper.cs          # JSON 序列化
│   ├── StageModInfo.xml       # Mod 元数据
│   └── LorAIHost.csproj
├── scripts/
│   └── setup-mcp.py           # MCP 一键注册脚本
├── pyproject.toml
└── requirements.txt
```

---

## MCP 工具一览

### 读取（Read）

| 工具 | 说明 |
|---|---|
| `health_check` | 检查 bridge 是否在线 |
| `get_game_state` | 完整游戏状态（navigation/battle/stages/inventory） |
| `get_battle_units` | 所有战斗单位详情（HP/骰子/手牌/buff） |
| `get_emotion_candidates` | 当前可选的情绪卡列表（index/name/state/level） |
| `get_stage_info` | 指定关卡信息 |
| `get_state_layer` | 获取状态的某一层（navigation/battle 等） |
| `get_static_data_list` | 列出静态数据文件 |
| `get_game_data_item` | 查询单个游戏数据（卡牌/书籍/敌人） |

### 战斗（Write）

| 工具 | 说明 |
|---|---|
| `start_battle` | 启动指定关卡（自动选 HP 最高的书 → 进战斗） |
| `battle_loop` | 自动打完整场战斗（出牌 + 情绪卡 + 异常恢复） |
| `play_round` | 手动打一轮（停骰子 → 出牌 → 确认） |
| `select_emotion_card` | 按索引选情绪卡 |

### 导航（Write）

| 工具 | 说明 |
|---|---|
| `start_game` | 标题画面点 Continue |
| `navigate` | 导航 UI 界面（Invitation/Sepiroth 等） |
| `skip_story` | 跳过剧情 |
| `click_battle_result` | 点击战斗结算 |
| `close_battle_scene` | 关闭战斗场景回主界面 |

### 调试（Debug）

| 工具 | 说明 |
|---|---|
| `list_methods` | 列出游戏对象的方法（反射） |
| `call_method` | 调用任意游戏方法（反射） |
| `kill_all_enemy` | 秒杀全部敌人（测试用） |

---

## 战斗 Phase 流转

```
RoundStartPhase_UI         <- 回合开始动画 (SkipRoundStartUI 跳过)
    |
RoundStartPhase_System     <- 速度骰掷骰 (StopSpeedDiceRoll 推进)
    |
SortUnitPhase -> DrawCardPhase -> ApplyEnemyCardPhase  <- 自动
    |
ApplyLibrarianCardPhase    <- 玩家装卡 (playBattleRound: autoPlay + confirm)
    |
ArrangeEquippedCards       <- 卡牌排序 (可能卡住 -> forceAdvancePhase)
    |
ActivateStartBattleEffect -> SetCurrentDiceAction -> CheckFarAreaPlay
    |
MoveUnits -> WaitUnitsArrive -> CheckParrying/CheckOneSideAction
    |
ProcessViewAction          <- 拼点演出 (自动循环)
    |
RoundEndPhase              <- 回合结束 (可能弹情绪卡选择 UI)
    |                         ^ 卡住时调用 getEmotionCandidates + selectEmotionCard
EndBattle / 回到 RoundStartPhase_UI
```

---

## C# HTTP API

通过 `POST http://localhost:17127/action` 调用：

### 基础 Action

| Action | 参数 | 说明 |
|---|---|---|
| `navigate` | phase | 导航 UI 界面 |
| `startGame` | - | 点击标题 Continue |
| `startBattle` | - | 从 BattleSetting 开始战斗 |
| `autoPlay` | - | 游戏自带自动出牌 |
| `playBattleRound` | - | autoPlay + confirm 一步到位 |
| `confirmCards` | - | 确认出牌 |
| `endBattle` | - | 结束战斗 |
| `closeBattleScene` | - | 关闭战斗场景 |
| `clickBattleResult` | - | 点击结算画面 |
| `skipStory` | - | 跳过剧情 |
| `killAllEnemy` | - | 秒杀全部敌人 |
| `getStageInfo` | stageId | 获取关卡信息 |
| `callMethod` | type, method, args | 反射调用任意方法 |

### 高级 Action

| Action | 参数 | 说明 |
|---|---|---|
| `prepareBattle` | stageId | 自动选 HP 最高的 5 本书 + PrepareBattle |
| `getBattleUnits` | - | 导出所有战斗单位数据 |
| `getEmotionCandidates` | - | 获取情绪卡候选列表 |
| `selectEmotionCard` | index | 按索引选情绪卡，自动处理 SelectOne |
| `forceAdvancePhase` | phase | 强制推进卡住的 phase |

---

## 配置

环境变量：

| 变量 | 默认值 | 说明 |
|---|---|---|
| `LOR_API_BASE_URL` | `http://localhost:17127` | 游戏 bridge 地址 |
| `LOR_MCP_TOOL_PROFILE` | `guided` | 工具集（`guided` 加载战斗自动化 tools） |

---

## 已知限制

- `prepareBattle` 需要已加载的战斗场景（至少通关一次教程）
- 游戏运行时 DLL 被锁定，更新 Mod 需先关闭游戏
- `clearCount` 在导出的 availableStages 中始终为 0
- `/state` 里的 `playerUnits`/`enemyUnits` 可能为空，战斗单位详情用 `getBattleUnits`
- 情绪卡选择默认用简单启发式（正面优先 > 等级高优先），可由 LLM 覆盖

---

## 许可证

[AGPL-3.0-only](LICENSE)
