# LorAIAgent Mod HTTP API 规范

> 本 API 由游戏内 Mod 暴露，MCP Server 通过它读取状态和执行动作。
> 默认监听 `127.0.0.1:17127`（与 `LaunchWithMod.bat` 一致）。

---

## 基础约定

- 所有响应均为 `application/json; charset=utf-8`
- 成功响应：`{ "ok": true, "data": ... }`
- 错误响应：`{ "ok": false, "error": { "code": "...", "message": "...", "details": ... } }`
- 所有 `/action` 调用都是**同步阻塞**的，等游戏主线程执行完再返回

---

## 端点清单

### `GET /health`

健康检查，确认 Mod 已加载。

**响应示例**：
```json
{
  "ok": true,
  "data": {
    "service": "lor-ai-agent",
    "mod_version": "0.1.0",
    "protocol_version": "2025-06-11-v1",
    "game_version": "1.1.0.6b",
    "status": "ready"
  }
}
```

---

### `GET /state`

返回当前完整游戏状态。

**响应示例（战斗中）**：
```json
{
  "ok": true,
  "data": {
    "scene": "RECEPTION",
    "phase": "DICE_ASSIGN",
    "stage": {
      "id": "STAGE_FINGER",
      "name": "食指",
      "act": 1,
      "max_act": 5,
      "scene_count": 3
    },
    "floor": {
      "id": "HISTORY",
      "name": "历史层",
      "emotion_level": 2,
      "ego_available": ["CYBERBONE"]
    },
    "librarians": [
      {
        "index": 0,
        "name": "Malkuth",
        "current_hp": 120,
        "max_hp": 120,
        "current_break": 80,
        "max_break": 80,
        "light": 3,
        "max_light": 4,
        "emotion_level": 2,
        "emotion_coins": 1,
        "speed_dice": [
          {"index": 0, "value": 5, "assigned": false},
          {"index": 1, "value": 3, "assigned": false}
        ],
        "hand": [
          {"index": 0, "id": "PAGE_STRIKE", "name": "厮打", "cost": 0}
        ]
      }
    ],
    "guests": [...],
    "unassigned_dice": [...],
    "assigned_dice": [...],
    "pending_clashes": [...],
    "playable_pages": [...],
    "available_actions": ["assign_speed_die", "equip_combat_page", "use_ego_page"],
    "agent_view": {...}
  }
}
```

---

### `GET /actions/available`

返回当前可用的动作描述列表，包含参数要求。

**响应示例**：
```json
{
  "ok": true,
  "data": {
    "scene": "RECEPTION",
    "phase": "DICE_ASSIGN",
    "actions": [
      {
        "name": "assign_speed_die",
        "description": "将一颗速度骰分配给目标",
        "requires_unit_index": true,
        "requires_die_index": true,
        "requires_target_index": true,
        "requires_page_index": false
      },
      {
        "name": "equip_combat_page",
        "description": "给速度骰装备战斗书页",
        "requires_unit_index": true,
        "requires_die_index": true,
        "requires_target_index": false,
        "requires_page_index": true
      }
    ]
  }
}
```

---

### `POST /action`

执行一个动作。

**请求体**：
```json
{
  "action": "assign_speed_die",
  "unit_index": 0,
  "die_index": 0,
  "target_index": 2,
  "page_index": null,
  "option_index": null,
  "client_context": {
    "source": "mcp",
    "tool_name": "act"
  }
}
```

**响应示例（成功）**：
```json
{
  "ok": true,
  "data": {
    "action": "assign_speed_die",
    "success": true,
    "new_phase": "DICE_ASSIGN",
    "message": "Speed die assigned"
  }
}
```

**响应示例（失败）**：
```json
{
  "ok": false,
  "error": {
    "code": "invalid_action",
    "message": "Target index out of range",
    "details": {"target_index": 5, "guest_count": 3},
    "retryable": false
  }
}
```

---

### `GET /events/stream`

SSE 事件流，用于减少轮询。

**事件类型**：
- `screen_changed` — 场景切换
- `phase_changed` — 战斗阶段切换
- `available_actions_changed` — 可用动作变化
- `dice_rolled` — 速度骰已掷
- `clash_started` — 拼点开始
- `clash_resolved` — 拼点结束
- `unit_downed` — 单位倒下/混乱
- `emotion_level_up` — 情感升级
- `stage_cleared` — 接待通关
- `game_over` — 游戏结束

**SSE 示例**：
```text
event: phase_changed
data: {"phase": "DICE_ASSIGN", "scene_count": 3}

id: 42
event: dice_rolled
data: {"unit_index": 0, "dice": [{"index": 0, "value": 5}, {"index": 1, "value": 3}]}
```

---

### `GET /data/{collection}`

返回游戏的静态元数据。

**支持的 collection**：
- `combat_pages` — 战斗书页
- `key_pages` — 钥匙页
- `guests` — 来宾接待
- `abnormalities` — 异常体
- `floors` — 楼层
- `passives` — 被动能力
- `ego_pages` — EGO 书页
- `stages` — 全部接待关卡

**响应示例**：
```json
{
  "ok": true,
  "data": {
    "PAGE_STRIKE": {
      "id": "PAGE_STRIKE",
      "name": "厮打",
      "rarity": "common",
      "cost": 0,
      "dice": [
        {"min": 1, "max": 4, "type": "slash", "power": 0}
      ],
      "abilities": []
    }
  }
}
```

---

## 错误码表

| Code | HTTP | 含义 |
|---|---|---|
| `invalid_request` | 400 | 请求参数错误 |
| `invalid_action` | 409 | 当前阶段不允许该动作 |
| `invalid_target` | 409 | 目标不合法 |
| `not_found` | 404 | 端点或 collection 不存在 |
| `internal_error` | 500 | Mod 内部错误 |
| `game_not_ready` | 503 | 游戏尚未加载到可玩状态 |

---

## 状态转换图

```
MENU
  ↓ open_character_select / select_reception
FLOOR_SELECT
  ↓ confirm_floor / assign_librarians
RECEPTION (SPEED_ROLL)
  ↓ dice_rolled event
RECEPTION (DICE_ASSIGN)
  ↓ assign_speed_die
RECEPTION (CARD_SELECT)
  ↓ equip_combat_page
RECEPTION (CLASH_RESOLVE)
  ↓ clash_resolved event
RECEPTION (EMOTION_COIN)
  ↓ burn_emotion_coin / skip
RECEPTION (REWARD)
  ↓ select_reward
MENU / NEXT_RECEPTION
```
