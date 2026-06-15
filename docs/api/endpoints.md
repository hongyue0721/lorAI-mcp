# LorAIHost HTTP API

> C# Mod 在游戏内启动的 HTTP 服务，默认监听 `localhost:17127`。

---

## 端点清单

### `GET /health`

健康检查。

**响应**：
```json
{
  "status": "ok",
  "version": "1.0.0",
  "requests": 42
}
```

---

### `GET /state`

返回完整游戏状态，包含以下层级：

- `meta` — 时间戳、游戏版本
- `navigation` — 当前 UI phase、场景、Sephirah
- `progression` — 章节、已开 Sephirah、图书馆等级
- `floors` — 所有楼层详情（等级、单位、编队）
- `inventory` — 卡牌、书籍、邀请书
- `availableStages` — 所有可用关卡（带缓存，10 秒 TTL）
- `battle` — 战斗状态（仅 `inBattle=true` 时有详细数据）

**响应**：
```json
{
  "meta": { "timestamp": "...", "gameVersion": "1.0" },
  "navigation": {
    "currentUIPhase": "Sephirah",
    "currentSephirah": "Malkuth",
    "activeScene": "Main"
  },
  "battle": {
    "inBattle": false
  }
}
```

---

### `GET /state/{layer}`

返回单个状态层。

支持的 layer：`navigation`、`progression`、`floors`、`inventory`、`availablestages`、`battle`

---

### `GET /static`

列出 `StaticDataExport` mod 导出的静态数据文件。

---

### `GET /static/{name}`

读取单个静态数据文件（如 `cards`、`books`、`enemies`、`passives`）。

**安全限制**：文件名不允许包含 `..`、`/`、`\`。

---

### `POST /action`

执行游戏动作。请求在 Unity 主线程上排队执行。

**请求体**：
```json
{
  "action": "startBattle",
  "stageId": 2
}
```

**响应（即时 action）**：
```json
{
  "status": "ok",
  "action": { "action": "startBattle", "stageId": 2 },
  "result": { "success": true, "message": "Battle started via UIBattleSettingPanel" },
  "state": { ... }
}
```

**响应（deferred action，如 `runStage`）**：

HTTP 响应会被挂起，直到协程完成或超时（30 秒）。完成后返回：
```json
{
  "status": "ok",
  "result": { "success": true, "message": "runStage completed for stage 2" },
  "state": { ... }
}
```

**错误响应**：
```json
{
  "status": "error",
  "action": { ... },
  "result": { "success": false, "error": "StageController.Instance is null" }
}
```

---

### `GET /action-status`

查看 deferred action 的完成状态（保留最近 50 条）。

---

## Action 列表

### 导航

| Action | 参数 | 说明 |
|---|---|---|
| `navigate` | `phase` | 导航 UI 界面 |
| `selectSephirah` | `sephirah` | 选择 Sephirah 楼层 |
| `getFloor` | `sephirah` | 获取楼层信息 |
| `startGame` | — | 点击标题 Continue |

### 战斗

| Action | 参数 | 说明 |
|---|---|---|
| `startStage` | `stageId` | 在邀请面板上设置关卡 |
| `runStage` | `stageId` | 完整流程（deferred） |
| `prepareBattle` | `stageId` | 自动选书 + PrepareBattle |
| `startBattle` | — | 从 BattleSetting 开始 |
| `autoPlay` | — | 自动出牌 |
| `playBattleRound` | — | autoPlay + confirm 原子操作 |
| `confirmCards` | — | 确认出牌（仅 ApplyLibrarianCardPhase） |
| `endBattle` | — | 结束战斗 |
| `closeBattleScene` | — | 关闭战斗场景 |
| `clickBattleResult` | — | 点击结算 |
| `gameOver` | `isWin`, `isBackButton` | 触发 GameOver |
| `killAllEnemy` | — | 秒杀（调试） |
| `getStageInfo` | `stageId` | 关卡状态 |

### 高级

| Action | 参数 | 说明 |
|---|---|---|
| `getBattleUnits` | — | 所有战斗单位详细数据 |
| `getEmotionCandidates` | — | 情绪卡候选列表 |
| `selectEmotionCard` | `index` | 选情绪卡 |
| `forceAdvancePhase` | `phase` | 强制推进卡住的 phase |

### 剧情

| Action | 参数 | 说明 |
|---|---|---|
| `skipStory` | — | 跳过剧情 |
| `endStory` | `forcely` | 结束剧情 |
| `advanceStory` | — | 推进剧情 |

### 调试

| Action | 参数 | 说明 |
|---|---|---|
| `listMethods` | `type` | 列出类型方法 |
| `callMethod` | `type`, `method`, `args` | 反射调用 |
| `getGameState` | — | 诊断 singleton 状态 |

---

## 性能特性

- 请求队列每帧最多处理 2 个，防止帧卡顿
- availableStages 缓存 10 秒
- deferred action 结果上限 50 条，超出自动清理
