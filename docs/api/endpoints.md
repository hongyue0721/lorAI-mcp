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

- `meta` — 时间戳、游戏版本、`stateVersion`、`protocolVersion`
- `availableActions` — 当前可执行动作集合，与 `GET /actions/available`、`POST /action` 门控同一判定源
- `navigation` — 当前 UI phase、场景、Sephirah
- `progression` — 章节、已开 Sephirah、图书馆等级
- `floors` — 所有楼层详情（等级、单位、编队）
- `inventory` — 卡牌、书籍、邀请书
- `availableStages` — 所有可用关卡（带缓存，10 秒 TTL）
- `battle` — 战斗状态（仅 `inBattle=true` 时有详细数据）

**响应**：
```json
{
  "meta": { "timestamp": "...", "gameVersion": "1.0", "stateVersion": 1, "protocolVersion": "1.1.0" },
  "availableActions": ["navigate", "selectSephirah", "getFloor", "getGameState"],
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

### `GET /actions/available`

合法动作查询端点。基于当前游戏真实状态判定哪些动作可执行（ValidActions(s_t)），
判定逻辑集中在 `ActionAvailability`（纯函数 + 静态注册表），不依赖 LLM/检索/启发式。

**响应**：
```json
{
  "stateVersion": 1,
  "protocolVersion": "1.1.0",
  "state": {
    "activeScene": "Battle",
    "currentUIPhase": "Battle",
    "battlePhase": "ApplyLibrarianCardPhase",
    "inBattle": true
  },
  "availableActions": ["autoPlay", "confirmCards", "playBattleRound", "getBattleUnits", "getStageInfo", "getGameState"],
  "actions": [
    { "action": "autoPlay", "category": "agent", "available": true, "reasonCode": "ok" },
    { "action": "selectEmotionCard", "category": "agent", "available": false, "reasonCode": "emotion_select_inactive" },
    { "action": "killAllEnemy", "category": "debug", "available": false, "reasonCode": "ok" }
  ]
}
```

约定：
- `category`: `agent`（改变游戏状态的正式动作）/ `query`（只读）/ `debug`（反射、作弊、强制推进）
- **debug 动作永远 `available=false`**（不被误当作普通玩家操作），但 `POST /action` 仍可直接执行它们（保留开发能力）
- 状态不可判定时按 `false` 处理，绝不猜测
- `reasonCode` 是机器短码，不是文案

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

**错误契约（四类可区分）**：

| 类别 | HTTP | 响应 |
|---|---|---|
| 未知动作 | `400` | `{"error":"unknown_action","action":...,"knownActions":[...]}` |
| 已知但当前状态非法 | `409` | `{"error":"invalid_action","action":...,"reasonCode":...,"availableActions":[...],"state":{...}}` |
| 动作执行失败（业务） | `200` | `{"status":"error","result":{"success":false,"error":...}}` |
| 内部异常 | `200` | `{"status":"error","result":{"code":"internal_error","error":...,"stack":...}}` |

`409` 的门控判定与 `GET /actions/available` / `/state.availableActions` 完全同源；
`debug` 类动作不受门控（可直接执行），但永不出现在 `availableActions`。

---

### `GET /action-status`

查看 deferred action 的完成状态（保留最近 50 条）。

---

## Action 分类与可用性条件

可用性由 `ActionAvailability` 注册表集中判定；下表的条件是判定条件的忠实转述。
`s.ActiveScene`/`s.BattlePhase` 等指 `GameStateFacts` 采集字段。

### Agent（普通玩家操作，计入 availableActions）

| Action | Category | Availability condition |
|---|---|---|
| `startGame` | agent | ActiveScene=Title 且 UITitleController 可用 |
| `navigate` | agent | ActiveScene=Main 且 UIController 就绪 且 不在战斗 |
| `selectSephirah` | agent | 同 navigate |
| `startStage` | agent | Main + 不在战斗 + LibraryModel 就绪 + UIInvitationPanel 在场 |
| `prepareBattle` | agent | 同 startStage |
| `runStage` | agent | 同 startStage（宏观复合动作，内部自带跳过剧情） |
| `startBattle` | agent | UIBattleSettingPanel 在场 且 不在战斗 |
| `autoPlay` | agent | 战斗中 且 Phase=ApplyLibrarianCardPhase |
| `confirmCards` | agent | 同 autoPlay |
| `playBattleRound` | agent | 同 autoPlay |
| `selectEmotionCard` | agent | levelup UI enabled 且存在活跃候选 |
| `endBattle` | agent | StageController 战斗窗口内 且 Phase=EndBattle |
| `closeBattleScene` | agent | ActiveScene=Battle 且 Phase=EndBattle |
| `clickBattleResult` | agent | UIBattleResultPanel 在场 且 ActiveScene=Battle |
| `skipStory` / `endStory` / `advanceStory` | agent | ActiveScene=Story 或 Phase=BattleStoryPhase |

### Query（只读，语义 = 当前状态下能产出有效数据）

| Action | Category | Availability condition |
|---|---|---|
| `getFloor` | query | LibraryModel 就绪 |
| `getStageInfo` | query | StageController 存在且 battleState≠None |
| `getBattleUnits` | query | 战斗中 或 ActiveScene=Battle |
| `getEmotionCandidates` | query | 战斗中 且 Phase=RoundEndPhase |
| `getGameState` | query | 总是 |

### Debug（可执行，默认不计入 availableActions）

| Action | Category | 说明 |
|---|---|---|
| `killAllEnemy` | debug | 秒杀全体敌人 |
| `gameOver` | debug | 直接触发 GameOver |
| `forceAdvancePhase` | debug | 反射写 `_phase` 强制推进 |
| `listMethods` / `callMethod` | debug | 任意反射读写 |

---

## 性能特性

- 请求队列每帧最多处理 2 个，防止帧卡顿
- availableStages 缓存 10 秒
- deferred action 结果上限 50 条，超出自动清理
